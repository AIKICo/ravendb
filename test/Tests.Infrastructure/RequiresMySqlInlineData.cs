using System.Collections.Generic;
using System.Reflection;
using Raven.Server.SqlMigration;
using Tests.Infrastructure.ConnectionString;
using Xunit.Sdk;

namespace Tests.Infrastructure
{
    public class RequiresMySqlInlineData : DataAttribute
    {
        public RequiresMySqlInlineData()
        {
            if (RavenTestHelper.SkipIntegrationTests)
            {
                Skip = RavenTestHelper.SkipIntegrationMessage;
                return;
            }
            
            if (RavenTestHelper.IsRunningOnCI)
                return;

            if (MySqlConnectionString.Instance.CanConnect == false)
                Skip = "Test requires MySQL database";
        }

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            return new[]
            {
#pragma warning disable CS0618 // Type or member is obsolete
                new object[] { MigrationProvider.MySQL_MySql_Data }, 
#pragma warning restore CS0618 // Type or member is obsolete
                new object[] { MigrationProvider.MySQL_MySqlConnector }
            };
        }
    }
}
