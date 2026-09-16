open DownloadUtils

let [<Literal>] stevneFileName = "20260822Kolbotn_SkiSvOmmingRACUPSuperSprintStevne.xml"
                                         
let [<Literal>] targetDir = @"D:\training\StevneMedley"

stevneFileName
|> parseFilename
|> printfn "%A"

getStevneoppsettFile targetDir stevneFileName  

let displayFunc (e: StevneXml.MeetSetup.Event, r:Result<StevneXml.MeetSetup.Event,string>) =
    match r with
    | Ok a -> $"Ok."
    | Error errorValue -> errorValue
    |> (printfn "%d. %s %A" e.EventNumber e.EventDescription)

ValidationMain.CheckMeetSetup  targetDir stevneFileName
|> Seq.iter displayFunc

let [<Literal>] dbDir = @"D:\training\StevneMedley\DbCopy"

for db in [ "person.mdb"; "rekorder.mdb"; "stevne.mdb" ] do
    AccessDbDump.dumpDatabase (System.IO.Path.Combine(dbDir, db)) 5

let displayDbResult (e: DbValidation.DbEvent, r: Result<DbValidation.DbEvent, string>) =
    match r with
    | Ok _ -> "Ok."
    | Error errorValue -> errorValue
    |> printfn "%d. %s [%A] %s" e.EventNumber e.Description e.Round

let meet, dbResults =
    DbValidation.checkLatestMeetSetup (System.IO.Path.Combine(dbDir, "stevne.mdb"))

printfn "=== validation straight from stevne.mdb: %d. %s (%s) ===" meet.Number meet.Name meet.Date
dbResults |> List.iter displayDbResult