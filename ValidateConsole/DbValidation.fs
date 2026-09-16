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

/// One row of Stevneoppsett. Carries the fields the ValidationMain rules need, not
/// all 56 columns.
type DbEvent =
    { MeetNumber: int
      EventNumber: int
      Description: string
      Round: Round
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

/// Victoria is not consistent about Jet booleans - Direktefinale is stored as 1 while
/// Senior is -1 - so any non-zero value counts as set.
let private flag (r: OleDbDataReader) (name: string) = intOr 0 r name <> 0

let private toDbEvent (r: OleDbDataReader) =
    { MeetNumber = intOr 0 r "Stevnenr"
      EventNumber = intOr 0 r "Stevnedistansenr"
      Description = (strOr "" r "Ovelsestekst").Trim()
      Round = Round.ofInt (intOr 0 r "Runde")
      PreliminaryEvent =
        match intOr 0 r "Forsoksovelse" with
        | 0 -> None
        | n -> Some n
      TypeOfFinal = intOr 0 r "Finaletype"
      DirectFinal = flag r "Direktefinale"
      Free = flag r "Gratis" }

/// A row of Stevnet. The database keeps every meet ever set up, not just the current one.
type Meet =
    { Number: int
      Name: string
      Date: string }

let listMeets (conn: OleDbConnection) : Meet list =
    query conn "SELECT Stevnenr, Stevnenavn, Stevnedato FROM Stevnet ORDER BY Stevnenr" (fun r ->
        { Number = intOr 0 r "Stevnenr"
          Name = (strOr "" r "Stevnenavn").Trim()
          Date = (strOr "" r "Stevnedato").Trim() })

/// Highest Stevnenr, which is the most recently set up meet.
let latestMeet (conn: OleDbConnection) : Meet =
    match listMeets conn with
    | [] -> failwith "Stevnet is empty - no meet to validate"
    | meets -> meets |> List.maxBy (fun m -> m.Number)

/// Events of one meet. Stevneoppsett holds every meet, and Stevnedistansenr is only
/// unique within a Stevnenr, so validating without filtering mixes meets together.
let readEvents (conn: OleDbConnection) (meetNumber: int) : DbEvent list =
    query
        conn
        $"SELECT Stevnenr, Stevnedistansenr, Ovelsestekst, Runde, Forsoksovelse,
                 Finaletype, Direktefinale, Gratis
          FROM Stevneoppsett
          WHERE Stevnenr = {meetNumber}
          ORDER BY Stevnedistansenr"
        toDbEvent

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

let private check (e: DbEvent) = e, finalRoundShouldOnlyBeOnFinalEvents e

let checkMeetSetup (dbPath: string) (meetNumber: int) =
    withConnection dbPath (fun conn -> readEvents conn meetNumber |> List.map check)

/// Validates the most recently set up meet, and reports which one that was.
let checkLatestMeetSetup (dbPath: string) =
    withConnection dbPath (fun conn ->
        let meet = latestMeet conn
        meet, readEvents conn meet.Number |> List.map check)
