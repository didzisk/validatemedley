/// Table metadata for the Victoria Access databases: what tables exist,
/// what columns they have, how many rows they hold.
module AccessDbSchema

open System
open System.Data
open System.Data.OleDb

type ColumnInfo =
    { Name: string
      DataType: string
      Size: int }

type TableInfo =
    { Name: string
      Columns: ColumnInfo list
      RowCount: int }

/// User tables only - the MSys* system tables are filtered out by TABLE_TYPE.
let listTables (conn: OleDbConnection) : string list =
    use schema = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, [| null; null; null; box "TABLE" |])

    schema.Rows
    |> Seq.cast<DataRow>
    |> Seq.map (fun r -> string r.["TABLE_NAME"])
    |> Seq.sort
    |> List.ofSeq

let columns (conn: OleDbConnection) (table: string) : ColumnInfo list =
    use cmd = new OleDbCommand($"SELECT * FROM [{table}]", conn)
    use reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
    use schema = reader.GetSchemaTable()

    schema.Rows
    |> Seq.cast<DataRow>
    |> Seq.map (fun r ->
        { Name = string r.["ColumnName"]
          DataType = (r.["DataType"] :?> Type).Name
          Size = Convert.ToInt32 r.["ColumnSize"] })
    |> List.ofSeq

let rowCount (conn: OleDbConnection) (table: string) : int =
    use cmd = new OleDbCommand($"SELECT COUNT(*) FROM [{table}]", conn)
    Convert.ToInt32(cmd.ExecuteScalar())

let describeTable (conn: OleDbConnection) (table: string) : TableInfo =
    { Name = table
      Columns = columns conn table
      RowCount = rowCount conn table }
