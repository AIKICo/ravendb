﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Orders;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Smuggler;
using Raven.Client.ServerWide.Operations;
using Raven.Server.Config;
using Raven.Server.Utils;
using Sparrow.Backups;
using Sparrow.Server.Utils;
using Sparrow.Utils;
using Tests.Infrastructure;
using Voron.Impl.Backup;
using Xunit;
using Xunit.Abstractions;
using BackupUtils = Raven.Server.Utils.BackupUtils;

namespace SlowTests.Issues;

public class RavenDB_21523 : RavenTestBase
{
    public RavenDB_21523(ITestOutputHelper output) : base(output)
    {
    }

    [RavenTheory(RavenTestCategory.BackupExportImport)]
    [InlineData(null, null)]
    [InlineData(ExportCompressionAlgorithm.Gzip, null)]
    [InlineData(ExportCompressionAlgorithm.Zstd, null)]
    [InlineData(ExportCompressionAlgorithm.Gzip, CompressionLevel.Optimal)]
    [InlineData(ExportCompressionAlgorithm.Zstd, CompressionLevel.Optimal)]
    [InlineData(ExportCompressionAlgorithm.Gzip, CompressionLevel.Fastest)]
    [InlineData(ExportCompressionAlgorithm.Zstd, CompressionLevel.Fastest)]
    [InlineData(ExportCompressionAlgorithm.Gzip, CompressionLevel.SmallestSize)]
    [InlineData(ExportCompressionAlgorithm.Zstd, CompressionLevel.SmallestSize)]
    [InlineData(ExportCompressionAlgorithm.Gzip, CompressionLevel.NoCompression)]
    [InlineData(ExportCompressionAlgorithm.Zstd, CompressionLevel.NoCompression)]
    public async Task CanExportImport(ExportCompressionAlgorithm? algorithm, CompressionLevel? compressionLevel)
    {
        var backupPath = NewDataPath(suffix: "BackupFolder");
        IOExtensions.DeleteDirectory(backupPath);
        var exportFile = Path.Combine(backupPath, "export.ravendbdump");

        using (var store = GetDocumentStore(new Options
        {
            ModifyDatabaseRecord = record =>
            {
                record.Settings[RavenConfiguration.GetKey(x => x.ExportImport.CompressionAlgorithm)] = algorithm?.ToString();
                record.Settings[RavenConfiguration.GetKey(x => x.ExportImport.CompressionLevel)] = compressionLevel?.ToString();
            }
        }))
        {
            using (var session = store.OpenAsyncSession())
            {
                await session.StoreAsync(new Company { Name = "HR" }, "companies/1");
                await session.SaveChangesAsync();
            }

            var operation = await store.Smuggler.ExportAsync(new DatabaseSmugglerExportOptions(), exportFile);
            await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(15));

            Assert.True(File.Exists(exportFile));

            await using (var fileStream = File.OpenRead(exportFile))
            await using (var backupStream = await BackupUtils.GetDecompressionStreamAsync(fileStream))
            {
                var buffer = new byte[1024];
                var read = await backupStream.ReadAsync(buffer, 0, buffer.Length); // validates if we picked appropriate decompression algorithm
                Assert.True(read > 0);

                switch (algorithm)
                {
                    case ExportCompressionAlgorithm.Gzip:
                        Assert.IsType<GZipStream>(backupStream);
                        break;
                    case null:
                    case ExportCompressionAlgorithm.Zstd:
                        if (compressionLevel == CompressionLevel.NoCompression)
                            Assert.IsType<BackupStream>(backupStream);
                        else
                            Assert.IsType<ZstdStream>(backupStream);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
                }
            }
        }

        using (var store = GetDocumentStore())
        {
            var operation = await store.Smuggler.ImportAsync(new DatabaseSmugglerImportOptions(), exportFile);
            await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(15));

            using (var session = store.OpenAsyncSession())
            {
                var company = await session.LoadAsync<Company>("companies/1");

                Assert.NotNull(company);
                Assert.Equal("HR", company.Name);
            }
        }
    }

    [RavenTheory(RavenTestCategory.BackupExportImport)]
    [InlineData(ExportCompressionAlgorithm.Gzip, ExportCompressionAlgorithm.Zstd)]
    [InlineData(ExportCompressionAlgorithm.Gzip, ExportCompressionAlgorithm.Gzip)]
    [InlineData(ExportCompressionAlgorithm.Gzip, null)]
    public async Task CanExportImport_Client(ExportCompressionAlgorithm defaultAlgorithm, ExportCompressionAlgorithm? exportAlgorithm)
    {
        var backupPath = NewDataPath(suffix: "BackupFolder");
        IOExtensions.DeleteDirectory(backupPath);
        var exportFile = Path.Combine(backupPath, "export.ravendbdump");

        using (var store = GetDocumentStore(new Options
        {
            ModifyDatabaseRecord = record => record.Settings[RavenConfiguration.GetKey(x => x.ExportImport.CompressionAlgorithm)] = defaultAlgorithm.ToString()
        }))
        {
            using (var session = store.OpenAsyncSession())
            {
                await session.StoreAsync(new Company { Name = "HR" }, "companies/1");
                await session.SaveChangesAsync();
            }

            var operation = await store.Smuggler.ExportAsync(new DatabaseSmugglerExportOptions { CompressionAlgorithm = exportAlgorithm }, exportFile);
            await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(15));

            Assert.True(File.Exists(exportFile));

            await using (var fileStream = File.OpenRead(exportFile))
            await using (var backupStream = await BackupUtils.GetDecompressionStreamAsync(fileStream))
            {
                var buffer = new byte[1024];
                var read = await backupStream.ReadAsync(buffer, 0, buffer.Length); // validates if we picked appropriate decompression algorithm
                Assert.True(read > 0);

                switch (exportAlgorithm)
                {
                    case null:
                    case ExportCompressionAlgorithm.Gzip:
                        Assert.IsType<GZipStream>(backupStream);
                        break;
                    case ExportCompressionAlgorithm.Zstd:
                        Assert.IsType<ZstdStream>(backupStream);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(defaultAlgorithm), defaultAlgorithm, null);
                }
            }
        }

        using (var store = GetDocumentStore())
        {
            var operation = await store.Smuggler.ImportAsync(new DatabaseSmugglerImportOptions(), exportFile);
            await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(15));

            using (var session = store.OpenAsyncSession())
            {
                var company = await session.LoadAsync<Company>("companies/1");

                Assert.NotNull(company);
                Assert.Equal("HR", company.Name);
            }
        }
    }

    [RavenTheory(RavenTestCategory.BackupExportImport)]
    [InlineData(null, null)]
    [InlineData(BackupCompressionAlgorithm.Gzip, CompressionLevel.Optimal)]
    [InlineData(BackupCompressionAlgorithm.Zstd, CompressionLevel.Optimal)]
    [InlineData(BackupCompressionAlgorithm.Gzip, CompressionLevel.Fastest)]
    [InlineData(BackupCompressionAlgorithm.Zstd, CompressionLevel.Fastest)]
    [InlineData(BackupCompressionAlgorithm.Gzip, CompressionLevel.SmallestSize)]
    [InlineData(BackupCompressionAlgorithm.Zstd, CompressionLevel.SmallestSize)]
    [InlineData(BackupCompressionAlgorithm.Gzip, CompressionLevel.NoCompression)]
    [InlineData(BackupCompressionAlgorithm.Zstd, CompressionLevel.NoCompression)]
    public async Task CanBackupRestore(BackupCompressionAlgorithm algorithm, CompressionLevel compressionLevel)
    {
        var backupPath = NewDataPath(suffix: "BackupFolder");
        IOExtensions.DeleteDirectory(backupPath);

        using (var store = GetDocumentStore(new Options
        {
            ModifyDatabaseRecord = record =>
            {
                record.Settings[RavenConfiguration.GetKey(x => x.Backup.CompressionAlgorithm)] = algorithm.ToString();
                record.Settings[RavenConfiguration.GetKey(x => x.Backup.CompressionLevel)] = compressionLevel.ToString();
            }
        }))
        {
            await RunBackupRestore(store, backupPath, BackupType.Backup, async lastFile =>
            {
                await using (var fileStream = File.OpenRead(lastFile))
                {
                    await using (var backupStream = await BackupUtils.GetDecompressionStreamAsync(fileStream))
                    {
                        var buffer = new byte[1024];
                        var read = await backupStream.ReadAsync(buffer, 0, buffer.Length); // validates if we picked appropriate decompression algorithm
                        Assert.True(read > 0);

                        switch (algorithm)
                        {
                            case BackupCompressionAlgorithm.Gzip:
                                Assert.IsType<GZipStream>(backupStream);
                                break;
                            case BackupCompressionAlgorithm.Zstd:
                                if (compressionLevel == CompressionLevel.NoCompression)
                                    Assert.IsType<BackupStream>(backupStream);
                                else
                                    Assert.IsType<ZstdStream>(backupStream);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
                        }
                    }
                }
            });
        }
    }

    [RavenTheory(RavenTestCategory.BackupExportImport)]
    [InlineData(null, null)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Deflate, CompressionLevel.Optimal)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Zstd, CompressionLevel.Optimal)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Deflate, CompressionLevel.Fastest)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Zstd, CompressionLevel.Fastest)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Deflate, CompressionLevel.SmallestSize)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Zstd, CompressionLevel.SmallestSize)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Deflate, CompressionLevel.NoCompression)]
    [InlineData(SnapshotBackupCompressionAlgorithm.Zstd, CompressionLevel.NoCompression)]
    public async Task CanSnapshotRestore(SnapshotBackupCompressionAlgorithm? algorithm, CompressionLevel? compressionLevel)
    {
        var backupPath = NewDataPath(suffix: "BackupFolder");
        IOExtensions.DeleteDirectory(backupPath);

        using (var store = GetDocumentStore(new Options
        {
            ModifyDatabaseRecord = record =>
            {
                record.Settings[RavenConfiguration.GetKey(x => x.Backup.SnapshotCompressionAlgorithm)] = algorithm?.ToString();
                record.Settings[RavenConfiguration.GetKey(x => x.Backup.SnapshotCompressionLevel)] = compressionLevel?.ToString();
            }
        }))
        {
            await RunBackupRestore(store, backupPath, BackupType.Snapshot, async lastFile =>
            {
                using (var zip = ZipFile.Open(lastFile, ZipArchiveMode.Read, System.Text.Encoding.UTF8))
                {
                    foreach (var entry in zip.Entries)
                    {
                        await using (var entryStream = entry.Open())
                        await using (var backupStream = FullBackup.GetDecompressionStream(entryStream))
                        {
                            switch (algorithm)
                            {
                                case SnapshotBackupCompressionAlgorithm.Deflate:
                                    Assert.IsType<BackupStream>(backupStream);
                                    break;
                                case null:
                                case SnapshotBackupCompressionAlgorithm.Zstd:
                                    if (compressionLevel == CompressionLevel.NoCompression)
                                        Assert.IsType<BackupStream>(backupStream);
                                    else
                                        Assert.IsType<ZstdStream>(backupStream);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
                            }
                        }
                    }
                }
            });
        }
    }

    private async Task RunBackupRestore(DocumentStore store, string backupPath, BackupType backupType, Func<string, Task> assertBackupCompressionAlgorithm)
    {
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new Company { Name = "HR" }, "companies/1");
            await session.SaveChangesAsync();
        }

        var config = Backup.CreateBackupConfiguration(backupPath, backupType);
        await Backup.UpdateConfigAndRunBackupAsync(Server, config, store);

        var databaseName = GetDatabaseName() + "restore";

        var backupDirectory = Directory.GetDirectories(backupPath).First();
        var files = Directory.GetFiles(backupDirectory)
            .Where(Raven.Client.Documents.Smuggler.BackupUtils.IsFullBackupOrSnapshot)
            .OrderBackups()
            .ToArray();

        var lastFile = files.Last();

        Assert.True(File.Exists(lastFile));

        await assertBackupCompressionAlgorithm(lastFile);

        var restoreConfig = new RestoreBackupConfiguration()
        {
            BackupLocation = backupDirectory,
            DatabaseName = databaseName,
            LastFileNameToRestore = lastFile
        };

        var restoreOperation = new RestoreBackupOperation(restoreConfig);
        var operation = await store.Maintenance.Server.SendAsync(restoreOperation);
        await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(30));

        using (var store2 = GetDocumentStore(new Options
        {
            CreateDatabase = false,
            ModifyDatabaseName = s => databaseName
        }))
        {
            using (var session = store2.OpenAsyncSession())
            {
                var company = await session.LoadAsync<Company>("companies/1");

                Assert.NotNull(company);
                Assert.Equal("HR", company.Name);
            }
        }
    }
}
