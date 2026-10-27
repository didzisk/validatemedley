open System.IO
open DownloadUtils

let [<Literal>] stevneFileName = "20260822Kolbotn_SkiSvOmmingRACUPSuperSprintStevne.xml"

let [<Literal>] targetDir = @"D:\training\StevneMedley"

//let [<Literal>] dbDir = @"D:\training\StevneMedley\DbCopy"
let [<Literal>] dbDir = @"D:\Victoria"

let displayFunc (e: StevneXml.MeetSetup.Event, r:Result<StevneXml.MeetSetup.Event,string>) =
    match r with
    | Ok a -> $"Ok."
    | Error errorValue -> errorValue
    |> (printfn "%d. %s %A" e.EventNumber e.EventDescription)

let displayDbResult (e: DbValidation.DbEvent, r: Result<DbValidation.DbEvent, string>) =
    match r with
    | Ok _ -> "Ok."
    | Error errorValue -> errorValue
    |> printfn "%d. %s [%A] %s" e.EventNumber e.Description e.Round

let runValidation () =
    stevneFileName
    |> parseFilename
    |> printfn "%A"

    getStevneoppsettFile targetDir stevneFileName

    // ValidationMain.CheckMeetSetup  targetDir stevneFileName
    // |> Seq.iter displayFunc

    for db in [ "person.mdb"; "rekorder.mdb"; "stevne.mdb" ] do
         AccessDbDump.dumpDatabase (Path.Combine(dbDir, db)) 5

    let splits = MeetConfig.load (MeetConfig.defaultPath ())

    let meet, dbResults =
        DbValidation.checkLatestMeetSetup splits (Path.Combine(dbDir, "stevne.mdb"))

    printfn "=== validation straight from stevne.mdb: %d. %s (%s) ===" meet.Number meet.Name meet.Date
    dbResults |> List.iter displayDbResult

/// Lists swimmers in a meet whose parent is a registered official. Fetches from medley.no,
/// so it needs network; the meet number defaults to the most recently set up meet.
let runDommerParents (meetNumber: int option) =
    let stevneDb = Path.Combine(dbDir, "stevne.mdb")
    let personDb = Path.Combine(dbDir, "person.mdb")

    let meet =
        match meetNumber with
        | Some n -> n
        | None -> (AccessDb.withConnection stevneDb DbValidation.latestMeet).Number

    printfn "=== dommerforeldre, stevne %d ===" meet
    let matches, unknownClubs = DommerParents.findParents stevneDb personDb meet
    DommerParents.printReport matches unknownClubs

match System.Environment.GetCommandLineArgs() |> List.ofArray with
| _ :: "dommerforeldre" :: rest ->
    rest
    |> List.tryHead
    |> Option.map int
    |> runDommerParents
| _ -> runValidation ()
