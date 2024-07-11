﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using FastTests.Utils;
using Nest;
using NetTopologySuite.Utilities;
using Raven.Client;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Session;
using Raven.Server.Documents;
using Raven.Server.ServerWide.Context;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using static Raven.Server.Smuggler.Documents.CounterItem;
using Assert = Xunit.Assert;

namespace SlowTests.Issues
{
    public class RavenDB_19481 : RavenTestBase
    {
        public RavenDB_19481(ITestOutputHelper output) : base(output)
        {
        }


        [RavenFact(RavenTestCategory.Smuggler)]
        public async Task ShouldntReapplyClusterTransactionTwiceInRestore()
        {
            DoNotReuseServer();

            var backupPath = NewDataPath(suffix: "BackupFolder");
            using (var store = GetDocumentStore())
            {
                const string id = "users/1";

                await RevisionsHelper.SetupRevisions(Server.ServerStore, store.Database);

                using (var session = store.OpenAsyncSession(new SessionOptions
                {
                    TransactionMode = TransactionMode.ClusterWide
                }))
                {
                    await session.StoreAsync(new User
                    {
                        Name = "Grisha"
                    }, id);
                    await session.SaveChangesAsync();
                }

                var stats = await store.Maintenance.SendAsync(new GetStatisticsOperation());
                Assert.Equal(1, stats.CountOfRevisionDocuments);


                var config = Backup.CreateBackupConfiguration(backupPath);
                var backupTaskId = await Backup.UpdateConfigAndRunBackupAsync(Server, config, store);
                await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: false, expectedEtag: 4);

                var databaseName = $"restored_database-{Guid.NewGuid()}";

                var clusterTransactions = new Dictionary<string, long>();
                Server.ServerStore.ForTestingPurposesOnly().BeforeExecuteClusterTransactionBatch = (dbName, batch) =>
                {
                    if (dbName == databaseName)
                    {
                        foreach (var clusterTx in batch)
                        {
                            var raftRequestId = clusterTx.Options.TaskId;

                            if (clusterTransactions.ContainsKey(clusterTx.Options.TaskId) == false)
                                clusterTransactions.Add(raftRequestId, 1);
                            else
                                clusterTransactions[raftRequestId]++;
                        }
                    }
                };

                using (Backup.RestoreDatabase(store,
                           new RestoreBackupConfiguration
                           {
                               BackupLocation = Directory.GetDirectories(backupPath).First(),
                               DatabaseName = databaseName
                           }))
                {
                }

                foreach (var kvp in clusterTransactions)
                {
                    var timesWasApplied = kvp.Value;
                    Assert.True(timesWasApplied <=1 , $"cluster transaction \"{kvp.Key}\" was reapplied more then once ({timesWasApplied} times)");
                }
            }
        }

        [RavenFact(RavenTestCategory.Smuggler)]
        public async Task RestoredDocThatCreatedByClusterWideTransactionShouldntHaveDeleteRevision()
        {
            var backupPath = NewDataPath(suffix: "BackupFolder");
            using var store = GetDocumentStore();

            const string id = "users/1";

            await RevisionsHelper.SetupRevisions(Server.ServerStore, store.Database);

            using (var session = store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
            {
                await session.StoreAsync(new Company { Name = "Grisha" }, id);
                await session.SaveChangesAsync();
            }

            var stats = await store.Maintenance.SendAsync(new GetStatisticsOperation());
            Assert.Equal(1, stats.CountOfRevisionDocuments);

            var config = Backup.CreateBackupConfiguration(backupPath);
            var backupTaskId = await Backup.UpdateConfigAndRunBackupAsync(Server, config, store);

            await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: false, expectedEtag: 2);

            var databaseName = $"restored_database-{Guid.NewGuid()}";

            using (Backup.RestoreDatabase(store,
                       new RestoreBackupConfiguration { BackupLocation = Directory.GetDirectories(backupPath).First(), DatabaseName = databaseName }))
            {
                using (var session = store.OpenAsyncSession(new SessionOptions()
                       {
                           Database = databaseName
                }))
                {
                    var user = await session.LoadAsync<Company>(id);
                    Assert.NotNull(user);

                    var revisionsMetadata = await session.Advanced.Revisions.GetMetadataForAsync(id);
                    foreach (var metadata in revisionsMetadata)
                    {
                        Assert.False(metadata.GetString(Constants.Documents.Metadata.Flags).Contains(DocumentFlags.DeleteRevision.ToString()),
                            $"Restored document \"{id}\" has \'DeleteRevision\'");
                    }
                }
            }

        }

        private class Company
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
        
        private class User
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
