/// Text rendering of Access database contents, for exploring what the old
/// Victoria databases actually hold. Read-only; no typed domain logic here.
module AccessDbDump

open System
open System.Data.OleDb
open System.Globalization
open System.IO
open AccessDb
open AccessDbSchema

/// Any value to a display string, culture-invariant so output is stable.
let render (value: obj) =
    match value with
    | null -> "<null>"
    | :? DBNull -> "<null>"
    | :? DateTime as d -> d.ToString("s", CultureInfo.InvariantCulture)
    | :? (byte array) as b -> $"<%d{b.Length} bytes>"
    | :? IFormattable as f -> f.ToString(null, CultureInfo.InvariantCulture)
    | v -> string v

/// First topN rows of a table, every field rendered as a string.
let readRows (conn: OleDbConnection) (table: string) (topN: int) : string list list =
    query conn $"SELECT TOP {topN} * FROM [{table}]" (fun r ->
        [ for i in 0 .. r.FieldCount - 1 -> render (r.GetValue i) ])

let private printTable (conn: OleDbConnection) (topN: int) (table: string) =
    let info = describeTable conn table
    printfn ""
    printfn "Table: %s (%d cols, %d rows)" info.Name info.Columns.Length info.RowCount

    let width = info.Columns |> List.fold (fun acc c -> max acc c.Name.Length) 0

    for c in info.Columns do
        printfn "  %-*s %s(%d)" width c.Name c.DataType c.Size

    for row in readRows conn table topN do
        printfn "  %s" (String.Join(" | ", row))

/// Prints every user table in the database: columns with types, then the first topN rows.
/// A table that fails to read is reported inline rather than aborting the dump.
let dumpDatabase (dbPath: string) (topN: int) =
    printfn "=== %s ===" (Path.GetFileName dbPath)

    try
        withConnection dbPath (fun conn ->
            for table in listTables conn do
                try
                    printTable conn topN table
                with ex ->
                    printfn "  !! %s: %s" table ex.Message)
    with ex ->
        printfn "  !! %s" ex.Message

    printfn ""
