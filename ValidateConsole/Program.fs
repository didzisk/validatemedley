module Program

open System
open System.IO
open CommandLine

let [<Literal>] defaultVictoriaDir = @"D:\Victoria"

/// Victoria writes the meet's XML export to <victoria>\<stevnenr>\meetsetup.xml. Validating
/// that file is the point - it is the copy that is about to be uploaded to medley.no.
let [<Literal>] meetSetupFileName = "meetsetup.xml"

// CommandLineParser cannot bind an F# option - it hands the raw string to a TypeConverter for
// FSharpOption<int> and throws. Nullable is its own idiom for "unset", so --meet uses that.
[<Verb("xml", HelpText = "Validate the meetsetup.xml Victoria generated for a meet, before it is uploaded.")>]
type XmlOptions =
    { [<Option('v', "victoria", Default = defaultVictoriaDir, HelpText = "Victoria installation folder.")>]
      Victoria: string
      [<Option('m', "meet", HelpText = "Meet number (Stevnenr). Defaults to the most recent meet.")>]
      Meet: Nullable<int> }

[<Verb("db", HelpText = "Validate a meet setup straight from stevne.mdb.")>]
type DbOptions =
    { [<Option('v', "victoria", Default = defaultVictoriaDir, HelpText = "Victoria installation folder.")>]
      Victoria: string
      [<Option('m', "meet", HelpText = "Meet number (Stevnenr). Defaults to the most recent meet.")>]
      Meet: Nullable<int> }

[<Verb("dump", HelpText = "Dump the schema and sample rows of the Victoria databases.")>]
type DumpOptions =
    { [<Option('v', "victoria", Default = defaultVictoriaDir, HelpText = "Victoria installation folder.")>]
      Victoria: string
      [<Option('r', "rows", Default = 5, HelpText = "Sample rows to print per table.")>]
      Rows: int }

[<Verb("dommerforeldre", HelpText = "List entrants whose parent is a registered official. Needs network access to medley.no.")>]
type DommerOptions =
    { [<Option('v', "victoria", Default = defaultVictoriaDir, HelpText = "Victoria installation folder.")>]
      Victoria: string
      [<Option('m', "meet", HelpText = "Meet number (Stevnenr). Defaults to the most recent meet.")>]
      Meet: Nullable<int> }

let private stevneDb victoria = Path.Combine(victoria, "stevne.mdb")
let private personDb victoria = Path.Combine(victoria, "person.mdb")

let private resolveMeet (victoria: string) (meet: Nullable<int>) =
    if meet.HasValue then meet.Value
    else (AccessDb.withConnection (stevneDb victoria) DbValidation.latestMeet).Number

let displayXmlResult (e: StevneXml.MeetSetup.Event, r: Result<StevneXml.MeetSetup.Event, string>) =
    match r with
    | Ok _ -> "Ok."
    | Error errorValue -> errorValue
    |> printfn "%d. %s %s" e.EventNumber e.EventDescription

let displayDbResult (e: DbValidation.DbEvent, r: Result<DbValidation.DbEvent, string>) =
    match r with
    | Ok _ -> "Ok."
    | Error errorValue -> errorValue
    |> printfn "%d. %s [%A] %s" e.EventNumber e.Description e.Round

let runXml (o: XmlOptions) =
    let meet = resolveMeet o.Victoria o.Meet
    let dir = Path.Combine(o.Victoria, string meet)

    printfn "=== validation of %s ===" (Path.Combine(dir, meetSetupFileName))
    ValidationMain.CheckMeetSetup dir meetSetupFileName
    |> Seq.iter displayXmlResult
    0

let runDb (o: DbOptions) =
    let splits = MeetConfig.load (MeetConfig.defaultPath ())
    let meet, results = DbValidation.checkMeetSetup splits (stevneDb o.Victoria) (resolveMeet o.Victoria o.Meet)

    printfn "=== validation straight from stevne.mdb: %d. %s (%s) ===" meet.Number meet.Name meet.Date
    results |> List.iter displayDbResult
    0

let runDump (o: DumpOptions) =
    for db in [ "person.mdb"; "rekorder.mdb"; "stevne.mdb" ] do
        AccessDbDump.dumpDatabase (Path.Combine(o.Victoria, db)) o.Rows
    0

/// Lists swimmers in a meet whose parent is a registered official. Fetches from medley.no,
/// so it needs network; the meet number defaults to the most recently set up meet.
let runDommerParents (o: DommerOptions) =
    let meet = resolveMeet o.Victoria o.Meet

    printfn "=== dommerforeldre, stevne %d ===" meet
    let matches, unknownClubs = DommerParents.findParents (stevneDb o.Victoria) (personDb o.Victoria) meet
    DommerParents.printReport matches unknownClubs
    0

[<EntryPoint>]
let main argv =
    let result =
        Parser.Default.ParseArguments<XmlOptions, DbOptions, DumpOptions, DommerOptions>(argv)

    match result with
    | :? Parsed<obj> as parsed ->
        match parsed.Value with
        | :? XmlOptions as o -> runXml o
        | :? DbOptions as o -> runDb o
        | :? DumpOptions as o -> runDump o
        | :? DommerOptions as o -> runDommerParents o
        | other -> failwithf "Unhandled verb %A" other
    | _ -> 1
