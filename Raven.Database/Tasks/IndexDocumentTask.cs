using System;
using log4net;
using Raven.Database.Indexing;
using Raven.Database.Json;
using Raven.Database.Extensions;

namespace Raven.Database.Tasks
{
    public class IndexDocumentTask : Task
    {
        public string Key { get; set; }

        private readonly ILog logger = LogManager.GetLogger(typeof (IndexDocumentTask));

        public override string ToString()
        {
            return string.Format("IndexDocumentTask - Key: {0}", Key);
        }

        public override void Execute(WorkContext context)
        {
            context.TransactionaStorage.Batch(actions =>
            {
                var doc = actions.DocumentByKey(Key);
                if (doc == null)
                {
                    actions.Commit();
                    return;
                }

                var json = JsonToExpando.Convert(doc.ToJson());

                foreach (var index in context.IndexDefinitionStorage.IndexNames)
                {
                    var viewFunc = context.IndexDefinitionStorage.GetIndexingFunction(index);
                    if (viewFunc == null)
                    {
                        continue; // index was removed before we could index it
                    }
                    try
                    {
                        logger.DebugFormat("Indexing document: '{0}' for index: {1}",doc.Key, index);
                        
                        context.IndexStorage.Index(index, viewFunc, new[] {json,},
                            context,actions);
                    }
                    catch (Exception e)
                    {
                        logger.WarnFormat(e, "Failed to index document '{0}' for index: {1}", doc.Key, index);
                     }
                }
                actions.Commit();
            });
        }
    }
}