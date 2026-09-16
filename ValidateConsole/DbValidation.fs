/// Validation of a meet setup read straight from stevne.mdb, rather than from the
/// XML that victoria.exe exports. The rules mirror those in ValidationMain, and the
/// error messages are kept identical so output is comparable between the two paths.
module DbValidation

open System
open System.Data.OleDb
open AccessDb

/// Stevneoppsett.Runde. The order comes from the enum table inside victoria.exe,
/// which lists UNDEFINED, DIRECTFINAL, PRELIMINARY, FINAL, SEMIFINAL, QUARTERFINAL,
/// 8FINAL, 16FINAL, 32FINAL - the same names victoria writes into the XML Round field.
/// A direct final is stored as Undefined plus the separate Direktefinale flag, which
/// is why the XML shows Round=UNDEFINED for events whose Sorting is FINAL.
type Round =
    | Undefined
    | DirectFinal
    | Preliminary
    | Final
    | SemiFinal
    | QuarterFinal
    | EighthFinal
    | SixteenthFinal
    | ThirtySecondFinal
    /// A value the enum table did not predict. Kept distinct so it cannot pass a rule
    /// by looking like Undefined.
    | UnknownRound of int

module Round =
    let ofInt =
        function
        | 0 -> Undefined
        | 1 -> DirectFinal
        | 2 -> Preliminary
        | 3 -> Final
        | 4 -> SemiFinal
        | 5 -> QuarterFinal
        | 6 -> EighthFinal
        | 7 -> SixteenthFinal
        | 8 -> ThirtySecondFinal
        | other -> UnknownRound other

/// Stevneoppsett.Damerherrer. The values are what Victoria's tri-state control stores,
/// which is why they are not 0/1/2.
type Sex =
    | Female
    | Male
    | Mixed
    | UnknownSex of int

module Sex =
    let ofInt =
        function
        | -1 -> Female
        | 0 -> Male
        | -2 -> Mixed
        | other -> UnknownSex other

/// One row of Stevneoppsett. Carries the fields the ValidationMain rules need, not
/// all 56 columns.
type DbEvent =
    { MeetNumber: int
      EventNumber: int
      Description: string
      Round: Round
      Sex: Sex
      /// Yngsteklasse / Eldsteklasse as birth years. Stored as text, and -1 on events
      /// that have no age class at all (relays), which reads as None.
      Youngest: int option
      Oldest: int option
      DistanceNumber: int
      /// From Distanser.Lagind via DistanceNumber. None when Distansenr has no row in
      /// Distanser, so a dangling reference cannot quietly look like an individual event.
      IsRelay: bool option
      SessionId: int
      PreliminaryEvent: int option
      TypeOfFinal: int
      DirectFinal: bool
      Free: bool }

let private intOr (dflt: int) (r: OleDbDataReader) (name: string) =
    let i = r.GetOrdinal name
    if r.IsDBNull i then dflt else Convert.ToInt32(r.GetValue i)

let private strOr (dflt: string) (r: OleDbDataReader) (name: string) =
    let i = r.GetOrdinal name
    if r.IsDBNull i then dflt else string (r.GetValue i)

/// Age-class years are text columns holding "2017", or "-1" when unset.
let private yearOr (r: OleDbDataReader) (name: string) =
    match Int32.TryParse((strOr "" r name).Trim()) with
    | true, year when year > 0 -> Some year
    | _ -> None

/// Victoria is not consistent about Jet booleans - Direktefinale is stored as 1 while
/// Senior is -1 - so any non-zero value counts as set.
let private flag (r: OleDbDataReader) (name: string) = intOr 0 r name <> 0

/// A row of Distanser: the distance catalogue shared by every meet.
type Distance =
    { Number: int
      /// Free text such as "50" or "4*50".
      Length: string
      IsRelay: bool }

/// Keyed by Distansenr. Lagind is -1 for team events and 0 for individual ones. It is a
/// better relay signal than the length text, which reads "300" for the relay Distansenr 37.
let readDistances (conn: OleDbConnection) : Map<int, Distance> =
    query conn "SELECT Distansenr, Lengde, Lagind FROM Distanser" (fun r ->
        let number = intOr 0 r "Distansenr"

        number,
        { Number = number
          Length = (strOr "" r "Lengde").Trim()
          IsRelay = flag r "Lagind" })
    |> Map.ofList

let private toDbEvent (distances: Map<int, Distance>) (r: OleDbDataReader) =
    let distanceNumber = intOr 0 r "Distansenr"

    { MeetNumber = intOr 0 r "Stevnenr"
      EventNumber = intOr 0 r "Stevnedistansenr"
      Description = (strOr "" r "Ovelsestekst").Trim()
      Round = Round.ofInt (intOr 0 r "Runde")
      Sex = Sex.ofInt (intOr 0 r "Damerherrer")
      Youngest = yearOr r "Yngsteklasse"
      Oldest = yearOr r "Eldsteklasse"
      DistanceNumber = distanceNumber
      IsRelay = distances |> Map.tryFind distanceNumber |> Option.map (fun d -> d.IsRelay)
      SessionId = intOr 0 r "SessionId"
      PreliminaryEvent =
        match intOr 0 r "Forsoksovelse" with
        | 0 -> None
        | n -> Some n
      TypeOfFinal = intOr 0 r "Finaletype"
      DirectFinal = flag r "Direktefinale"
      Free = flag r "Gratis" }

/// A row of Stevnet. The database keeps every meet ever set up, not just the current one.
/// The age fields are the ones the XML exports as WomenJunior/WomenSenior/MenJunior/MenSenior.
type Meet =
    { Number: int
      Name: string
      Date: string
      WomenJunior: int
      WomenSenior: int
      MenJunior: int
      MenSenior: int }

let listMeets (conn: OleDbConnection) : Meet list =
    query
        conn
        "SELECT Stevnenr, Stevnenavn, Stevnedato, Damerjunior, Damersenior,
                Herrerjunior, Herrersenior
         FROM Stevnet
         ORDER BY Stevnenr"
        (fun r ->
            { Number = intOr 0 r "Stevnenr"
              Name = (strOr "" r "Stevnenavn").Trim()
              Date = (strOr "" r "Stevnedato").Trim()
              WomenJunior = intOr 0 r "Damerjunior"
              WomenSenior = intOr 0 r "Damersenior"
              MenJunior = intOr 0 r "Herrerjunior"
              MenSenior = intOr 0 r "Herrersenior" })

/// Highest Stevnenr, which is the most recently set up meet.
let latestMeet (conn: OleDbConnection) : Meet =
    match listMeets conn with
    | [] -> failwith "Stevnet is empty - no meet to validate"
    | meets -> meets |> List.maxBy (fun m -> m.Number)

/// Events of one meet. Stevneoppsett holds every meet, and Stevnedistansenr is only
/// unique within a Stevnenr, so validating without filtering mixes meets together.
let readEvents (conn: OleDbConnection) (meetNumber: int) : DbEvent list =
    let distances = readDistances conn

    query
        conn
        $"SELECT Stevnenr, Stevnedistansenr, Ovelsestekst, Runde, Damerherrer,
                 Yngsteklasse, Eldsteklasse, Distansenr, SessionId, Forsoksovelse,
                 Finaletype, Direktefinale, Gratis
          FROM Stevneoppsett
          WHERE Stevnenr = {meetNumber}
          ORDER BY Stevnedistansenr"
        (toDbEvent distances)

/// Round=Finale tells the entry system the event is filled by qualification from a
/// preliminary event, so a preliminary event has to be named. Events meant to be
/// entered directly should use Direktefinale instead.
let finalRoundShouldOnlyBeOnFinalEvents (e: DbEvent) =
    if e.Round = Final then
        match e.PreliminaryEvent with
        | None -> Error $"{e.EventNumber}. Når runde = Finale, må forsøksøvelse spesifiseres"
        | Some _ -> Ok e
    else
        Ok e

/// Outside the finals, every individual event must use the meet-wide age span: youngest
/// equal to the meet's junior year, oldest one year under the senior year. Relays carry no
/// age class, so they are exempt.
///
/// Mirrors ValidationMain.seniorLimitShouldBeTheSameAsForMeet, including its choice to send
/// Mixed events down the men's branch, and its messages - so both paths stay comparable.
/// It does differ in one way on purpose: relays are detected through Distanser.Lagind rather
/// than by looking for "*" in the length text, which misses the relay Distansenr 37.
///
/// A meet configured in meets.json is instead split into two age bands at the cutoff year,
/// because Victoria cannot record that split itself. Unconfigured meets take the plain path.
let seniorLimitShouldBeTheSameAsForMeet (m: Meet) (split: MeetConfig.MeetSplit option) (e: DbEvent) =
    if e.Round = Final then
        Ok e
    else
        match e.IsRelay with
        | None ->
            Error
                $"{e.EventNumber}. Ukjent distanse (Distansenr {e.DistanceNumber}), kan ikke avgjøre om øvelsen er stafett"
        | Some true -> Ok e
        | Some false ->
            let junior, senior =
                match e.Sex with
                | Female -> m.WomenJunior, m.WomenSenior
                | Male
                | Mixed
                | UnknownSex _ -> m.MenJunior, m.MenSenior

            match split with
            | None ->
                if e.Youngest <> Some junior then
                    Error "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)"
                elif e.Oldest <> Some (senior + 1) then
                    Error "Ikke Finale, Eldst må være 1 år under stevnets senioralder (gjelder ikke stafett)"
                else
                    Ok e
            | Some split ->
                let isYoungBand = split.YoungSessions.Contains e.SessionId

                let band, expectedYoungest, expectedOldest =
                    if isYoungBand then
                        "yngste årsgruppe", junior, split.CutoffYear
                    else
                        "eldste årsgruppe", split.CutoffYear - 1, senior + 1

                if e.Youngest <> Some expectedYoungest then
                    Error $"Ikke Finale, Yngst må være {expectedYoungest} i {band} (aldersdelt stevne)"
                elif e.Oldest <> Some expectedOldest then
                    Error $"Ikke Finale, Eldst må være {expectedOldest} i {band} (aldersdelt stevne)"
                else
                    Ok e

let private check (m: Meet) (split: MeetConfig.MeetSplit option) (e: DbEvent) =
    let result =
        finalRoundShouldOnlyBeOnFinalEvents e
        |> Result.bind (seniorLimitShouldBeTheSameAsForMeet m split)

    e, result

let checkMeetSetup (splits: Map<int, MeetConfig.MeetSplit>) (dbPath: string) (meetNumber: int) =
    withConnection dbPath (fun conn ->
        match listMeets conn |> List.tryFind (fun m -> m.Number = meetNumber) with
        | None -> failwithf "No meet with Stevnenr %d in Stevnet" meetNumber
        | Some meet ->
            let split = splits |> Map.tryFind meetNumber
            readEvents conn meetNumber |> List.map (check meet split))

/// Validates the most recently set up meet, and reports which one that was.
let checkLatestMeetSetup (splits: Map<int, MeetConfig.MeetSplit>) (dbPath: string) =
    withConnection dbPath (fun conn ->
        let meet = latestMeet conn
        let split = splits |> Map.tryFind meet.Number
        meet, readEvents conn meet.Number |> List.map (check meet split))
