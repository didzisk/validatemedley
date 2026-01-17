open DownloadUtils

let [<Literal>] stevneFileName = "20260228Kolbotn_SkiSvOmmingRACUP.xml"
                                         
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