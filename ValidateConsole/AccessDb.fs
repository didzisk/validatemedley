/// Connection lifetime and raw query execution for the Victoria Access databases.
/// Table metadata lives in AccessDbSchema, text rendering in AccessDbDump.
module AccessDb

open System
open System.Data.OleDb
open System.IO

/// Tried in order. The DbCopy files are Jet 3 (Access 97), which every ACE version
/// rejects with "Cannot open a database created with a previous version of your
/// application" - only Jet 4.0 reads them, and only in a 32-bit process.
let Providers =
    [ "Microsoft.Jet.OLEDB.4.0"
      "Microsoft.ACE.OLEDB.16.0"
      "Microsoft.ACE.OLEDB.12.0" ]

let private tryOpen (dbPath: string) (provider: string) =
    let conn = new OleDbConnection($"Provider={provider};Data Source={dbPath};Mode=Read")
    try
        conn.Open()
        Ok conn
    with ex ->
        conn.Dispose()
        Error $"{provider}: {ex.Message}"

/// Opens the .mdb read-only, so no .ldb lock file is created next to it.
/// The caller owns the connection - bind it with `use`, or prefer withConnection.
let openConnection (dbPath: string) =
    if not (File.Exists dbPath) then
        failwithf "Access database not found: %s" dbPath

    let rec attempt tried remaining =
        match remaining with
        | [] ->
            failwithf
                "Could not open %s with any OLE DB provider (process is %s).\n%s"
                dbPath
                (if Environment.Is64BitProcess then "64-bit" else "32-bit")
                (tried |> List.rev |> String.concat "\n")
        | provider :: rest ->
            match tryOpen dbPath provider with
            | Ok conn -> conn
            | Error msg -> attempt (msg :: tried) rest

    attempt [] Providers

/// Opens once, runs f, always closes. Use this to batch several queries per open.
let withConnection (dbPath: string) (f: OleDbConnection -> 'a) : 'a =
    use conn = openConnection dbPath
    f conn

/// Runs sql and projects each row with map. The reader is positioned on the row,
/// so map should pull values out rather than hold on to the reader.
let query (conn: OleDbConnection) (sql: string) (map: OleDbDataReader -> 'a) : 'a list =
    use cmd = new OleDbCommand(sql, conn)
    use reader = cmd.ExecuteReader()

    [ while reader.Read() do
        yield map reader ]

let scalar<'a> (conn: OleDbConnection) (sql: string) : 'a =
    use cmd = new OleDbCommand(sql, conn)
    cmd.ExecuteScalar() |> unbox<'a>
