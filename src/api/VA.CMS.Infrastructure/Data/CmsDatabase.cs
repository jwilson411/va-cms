using PetaPoco;

namespace VA.CMS.Infrastructure.Data;

/// <summary>
/// PetaPoco database context for VA CMS. Registered as AddScoped&lt;CmsDatabase&gt; in DI.
/// All queries go through stored procedures — no raw DML from this layer.
/// </summary>
public class CmsDatabase : Database
{
    public CmsDatabase(string connectionString)
        : base(connectionString, Microsoft.Data.SqlClient.SqlClientFactory.Instance)
    {
    }
}
