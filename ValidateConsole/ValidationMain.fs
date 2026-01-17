module ValidationMain

open System.IO
open System.Text

let apply fResult xResult =
    match fResult, xResult with
    | Ok f, Ok x ->
        Ok (f x)
    | Error errs, Ok x ->
        Error errs
    | Ok f, Error errs ->
        Error errs
    | Error errs1, Error errs2 ->
        // concat both lists of errors
        Error (List.concat [errs1; errs2])
        
let (<!>) = Result.map
let (<*>) = apply

let finalRoundShouldOnlyBeOnFinalEvents (e:StevneXml.MeetSetup.Event)  =
    if e.Round = "FINAL" then
        match e.Preliminaryevent with
        | None -> Error $"{e.EventNumber}. Når runde = Finale, må forsøksøvelse spesifiseres"
        | Some _ -> Ok e
    else
        Ok e
        
let finalRoundShouldNotHaveYoungestEldest (e:StevneXml.MeetSetup.Event)  =
    if e.Round = "FINAL" then
        match e.Youngest with
        | None ->
            match e.Oldest with
            | None -> Ok e
            | Some _ -> Error $"{e.EventNumber}. Finale, Eldst er ikke tillatt"
        | Some _ -> Error $"{e.EventNumber}. Finale, Yngst er ikke tillatt"
    else
        Ok e

let seniorLimitShouldBeTheSameAsForMeet (m:StevneXml.MeetSetup.MeetSetUp) (e:StevneXml.MeetSetup.Event)  =
    if e.Round = "FINAL" then
        Ok e
    else if (string e.EventLength).Contains("*") then
             Ok e
    else if e.Sex = "FEMALE" then
             if e.Youngest <> Some m.WomenJunior then
                  Error "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)"
             else if e.Oldest <> Some (m.WomenSenior + 1) then
                  Error "Ikke Finale, Eldst må være 1 år under stevnets senioralder (gjelder ikke stafett)"
                  else
                      Ok e
        else 
             if e.Youngest <> Some m.MenJunior then
                  Error "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)"
             else if e.Oldest <> Some (m.MenSenior + 1) then
                  Error "Ikke Finale, Eldst må være 1 år under stevnets senioralder (gjelder ikke stafett)"
                  else
                      Ok e
    
let finalShouldBeFree (e:StevneXml.MeetSetup.Event)  =
    if e.Round = "FINAL" then
        match e.Free with
        | false -> Error "Finale, bør være gratis (Generelt 2 | Øvelsen skal ikke betales for)"
        | true  -> Ok e
    else
        Ok e
        
let CheckMeetSetup (targetDir:string) (filename:string) =
    let fullPath = Path.Combine [| targetDir; filename |]
    
    let encoding = Encoding.GetEncoding("ISO-8859-1");

    use reader = new StreamReader(fullPath, encoding)
    let currentMeet = StevneXml.MeetSetup.Load reader
    
    let validationFunc (e:StevneXml.MeetSetup.Event) =
        let res = 
            finalRoundShouldOnlyBeOnFinalEvents e
            |> Result.bind finalRoundShouldNotHaveYoungestEldest
            |> Result.bind (seniorLimitShouldBeTheSameAsForMeet currentMeet)
            |> Result.bind finalShouldBeFree
        e, res
    
    currentMeet.Events
    |> Seq.map validationFunc