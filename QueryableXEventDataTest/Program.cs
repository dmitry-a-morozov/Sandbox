using Microsoft.SqlServer.XEvent.Linq;
using System.Data.SqlClient;
using System;
using System.Threading;
using System.Linq;

class Program
{
    const string connectionString = "Data Source=.;Initial Catalog=master;Integrated Security=True";
    const string targetDatabase = "AdventureWorks2014";
    const string xeSession = "XE_Alter";
    static void Main()
    {
        CreateXeSession();
        var t = new Thread(GenerateFakeDDL);
        t.Start();
        ReadXeEvents();
    }

    static void GenerateFakeDDL(object _)
    {
        using (var conn = new SqlConnection(connectionString))
        {
            var cmd = conn.CreateCommand();
            try
            {
                conn.Open();
                conn.ChangeDatabase(targetDatabase);
                cmd.CommandText = @"
                        IF OBJECT_ID('dbo.MyView42') IS NOT NULL 
                            DROP VIEW dbo.MyView42;
                ";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE VIEW dbo.MyView42 AS SELECT Col1 = 42";
                cmd.ExecuteNonQuery();

                foreach (var i in Enumerable.Range(43, 10000))
                {
                    cmd.CommandText = $"ALTER VIEW dbo.MyView42 AS SELECT Col1={i + 1}";
                StartOver:
                    Console.WriteLine($"About to run {cmd.CommandText}. Enter [y] to continue [n] to stop.");
                    var input = Console.ReadLine();
                    if (input.ToLower() == "y")
                        cmd.ExecuteNonQuery(); 
                    else if (input.ToLower() == "y")
                        break;
                    else
                    { 
                        Console.WriteLine($"Unexpected input {input}. Try again.");
                        goto StartOver;
                    }
                }
            }
            finally
            {
                cmd.CommandText = "DROP VIEW dbo.MyView42";
                cmd.ExecuteNonQuery();
            }
        }
    }

    static void CreateXeSession()
    {
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            var createSession = $@"
                IF NOT EXISTS(SELECT * FROM sys.server_event_sessions WHERE name = '{xeSession}')
                BEGIN
                    CREATE EVENT SESSION [{xeSession}]
                    ON SERVER
                        ADD EVENT sqlserver.object_created
                        (
                            ACTION (sqlserver.database_name, sqlserver.sql_text)
                            WHERE(sqlserver.database_name = '{targetDatabase}')
                        ),
                        ADD EVENT sqlserver.object_deleted
                        (
                            ACTION (sqlserver.database_name, sqlserver.sql_text)
                            WHERE(sqlserver.database_name = '{targetDatabase}')
                        ),
		                ADD EVENT sqlserver.object_altered
                        (
                            ACTION (sqlserver.database_name, sqlserver.sql_text)
                            WHERE(sqlserver.database_name = '{targetDatabase}')
                        ),

                        --trash events to make buffer to flush
                        ADD EVENT sqlos.async_io_completed,
                        ADD EVENT sqlserver.sql_batch_completed,
                        ADD EVENT sqlserver.sql_batch_starting,
                        ADD EVENT sqlserver.sql_statement_completed,
                        ADD EVENT sqlserver.sql_statement_recompile,
                        ADD EVENT sqlserver.sql_statement_starting,
                        ADD EVENT sqlserver.sql_transaction,
                        ADD EVENT sqlserver.sql_transaction_commit_single_phase
                END
                IF NOT EXISTS(SELECT * FROM sys.dm_xe_sessions WHERE name='{xeSession}')
                BEGIN
                    ALTER EVENT SESSION [{xeSession}] ON SERVER STATE = START
                END
                ";
            var cmd = new SqlCommand(createSession, conn);
            cmd.ExecuteNonQuery();
        }
    }
    static void ReadXeEvents()
    {
        using (var events = new QueryableXEventData(connectionString, xeSession, EventStreamSourceOptions.EventStream, EventStreamCacheOptions.DoNotCache))
        {
            foreach (var x in events)
            {
                if (x.Name == "object_altered" || x.Name == "object_created" | x.Name == "object_deleted")
                {
                    PublishedEventField ddl_phase;
                    if (x.Fields.TryGetValue(nameof(ddl_phase), out ddl_phase))
                    {
                        var fs = x.Fields;
                        if (ddl_phase.Value.ToString() == "Commit")
                        {
                            Console.WriteLine($"\nEvent {x.Name}.\nDDL Phase: {fs["ddl_phase"].Value}.\nObject: id-{fs["object_id"].Value}; name-{fs["object_name"].Value}.\nDatabase: id-{fs["database_id"].Value}; name-{x.Actions["database_name"].Value}.\nSql text: {x.Actions["sql_text"].Value}");
                        }
                    }
                }

            }
        }
    }
}

