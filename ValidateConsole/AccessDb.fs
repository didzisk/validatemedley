/// Connection lifetime and raw query execution for the Victoria Access databases.
/// Table metadata lives in AccessDbSchema, text rendering in AccessDbDump.
module AccessDb

open System
open System.Data.OleDb
open System.IO

/// The Victoria databases are Jet 3 (Access 97). Every ACE version rejects that format
/// with "Cannot open a database created with a previous version of your application",
/// so Jet 4.0 is the only option - and it exists only as a 32-bit provider, which is
/// why this project targets win-x86. An .accdb would need ACE instead.
let [<Literal>] Provider = "Microsoft.Jet.OLEDB.4.0"

/// Opens the .mdb read-only, so no .ldb lock file is created next to it.
/// The caller owns the connection - bind it with `use`, or prefer withConnection.
let openConnection (dbPath: string) =
    if not (File.Exists dbPath) then
        failwithf "Access database not found: %s" dbPath

    let conn = new OleDbConnection($"Provider={Provider};Data Source={dbPath};Mode=Read")
    try
        conn.Open()
        conn
    with ex ->
        conn.Dispose()
        failwithf
            "Could not open %s with %s (process is %s; the provider is 32-bit only).\n%s"
            dbPath
            Provider
            (if Environment.Is64BitProcess then "64-bit" else "32-bit")
            ex.Message

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
