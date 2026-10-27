/// Finds, among the swimmers entered in a meet, those whose parent is a registered swim
/// official - so the club knows who it can ask to judge.
///
/// The two halves come from different places and are joined on surname and club:
///   * who is swimming: stevne.mdb Pamelder -> person.mdb Utover
///   * who is an official: medley.no, via MedleyDommere
///
/// Club numbering is not shared between them (Kolbotn IL is 75 in person.mdb and 73 on
/// medley), so clubs are matched by name.
module DommerParents

open System
open AccessDb

/// A person entered in the meet, as Victoria knows them.
type Swimmer =
    { Id: int
      FirstName: string
      LastName: string
      Club: string }

/// One line of the report.
type ParentMatch =
    { Swimmer: string
      Parent: string
      Club: string
      JudgeType: string
      Emails: string list
      Phones: string list }

/// Surnames are compared part by part, because Norwegian compound surnames are written
/// inconsistently between the two registers - a swimmer recorded as "Strøm Fredriksen" may
/// have a parent listed as plain "Fredriksen".
let surnameTokens (surname: string) =
    surname.Split([| ' '; '-' |], StringSplitOptions.RemoveEmptyEntries)
    |> Seq.map (fun part -> part.Trim().ToLowerInvariant())
    |> Seq.filter (fun part -> part <> "")
    |> Set.ofSeq

let private normalise (s: string) = s.Trim().ToLowerInvariant()

/// Club names come from two registers that spell them the same but not always with the same
/// padding or casing.
let sameClub (a: string) (b: string) = normalise a = normalise b

/// An official counts as a candidate parent when some part of the surname is shared - but not
/// when they are plainly the swimmer themselves, which happens because older swimmers are
/// often officials too.
let isParent (swimmerFirst: string) (swimmerLast: string) (official: MedleyDommere.Official) =
    let isSamePerson =
        normalise swimmerFirst = normalise official.FirstName
        && normalise swimmerLast = normalise official.LastName

    if isSamePerson then
        false
    else
        Set.intersect (surnameTokens swimmerLast) (surnameTokens official.LastName)
        |> Set.isEmpty
        |> not

/// Pairs swimmers with the officials in their own club who could be their parent.
let matchSwimmers (swimmers: Swimmer list) (officials: MedleyDommere.Official list) =
    [ for swimmer in swimmers do
          for official in officials do
              if sameClub swimmer.Club official.Club && isParent swimmer.FirstName swimmer.LastName official then
                  yield $"{swimmer.FirstName} {swimmer.LastName}", official ]

// ---------------------------------------------------------------- database side

let private intOr dflt (r: Data.OleDb.OleDbDataReader) (name: string) =
    let i = r.GetOrdinal name
    if r.IsDBNull i then dflt else Convert.ToInt32(r.GetValue i)

let private strOr dflt (r: Data.OleDb.OleDbDataReader) (name: string) =
    let i = r.GetOrdinal name
    if r.IsDBNull i then dflt else string (r.GetValue i)

/// Distinct people entered in one meet. Pamelder holds one row per entry, so a swimmer in
/// five events appears five times.
let private entrantIds (stevneDb: string) (meetNumber: int) =
    withConnection stevneDb (fun conn ->
        query conn $"SELECT DISTINCT IDnr FROM Pamelder WHERE Stevnenr = {meetNumber}" (fun r -> intOr 0 r "IDnr"))

/// Resolves entrants to names and club names. Pamelder.IDnr is person.mdb's Utover.Utovernr,
/// and Utover.Klubb is Klubber.Klubbnr.
let private readSwimmers (personDb: string) (ids: int list) =
    if ids.IsEmpty then
        []
    else
        let idList = String.Join(",", ids)

        withConnection personDb (fun conn ->
            query
                conn
                $"SELECT u.Utovernr, u.Fornavn, u.Etternavn, k.Klubbnavn
                  FROM Utover AS u INNER JOIN Klubber AS k ON u.Klubb = k.Klubbnr
                  WHERE u.Utovernr IN ({idList})"
                (fun r ->
                    { Id = intOr 0 r "Utovernr"
                      FirstName = (strOr "" r "Fornavn").Trim()
                      LastName = (strOr "" r "Etternavn").Trim()
                      Club = (strOr "" r "Klubbnavn").Trim() }))

/// The clubs a meet draws from, which is how few medley requests the report needs.
let clubsInMeet (swimmers: Swimmer list) =
    swimmers |> List.map (fun s -> s.Club) |> List.distinct |> List.sort

/// Runs the whole report for one meet.
///
/// Returns the matches, plus the clubs whose name medley does not recognise - those are
/// reported rather than dropped, because a silent miss looks exactly like "no officials".
let findParents (stevneDb: string) (personDb: string) (meetNumber: int) =
    let swimmers = entrantIds stevneDb meetNumber |> readSwimmers personDb
    let clubIds = MedleyDommere.clubIds ()

    let mutable unknownClubs = []
    let mutable matches = []

    for club in clubsInMeet swimmers do
        match clubIds |> Map.tryFind club with
        | None -> unknownClubs <- club :: unknownClubs
        | Some clubId ->
            let here = swimmers |> List.filter (fun s -> sameClub s.Club club)

            // only expand the officials who actually matched someone
            let isCandidate (o: MedleyDommere.Official) =
                here |> List.exists (fun s -> isParent s.FirstName s.LastName o)

            let withContacts = MedleyDommere.officialsWithContacts clubId isCandidate
            let contactOf = withContacts |> List.map (fun (o, c) -> o.Key, c) |> Map.ofList
            let officials = withContacts |> List.map fst

            for swimmerName, official in matchSwimmers here officials do
                let contact =
                    contactOf
                    |> Map.tryFind official.Key
                    |> Option.defaultValue { MedleyDommere.Emails = []; MedleyDommere.Phones = [] }

                matches <-
                    { Swimmer = swimmerName
                      Parent = $"{official.FirstName} {official.LastName}"
                      Club = official.Club
                      JudgeType = official.JudgeType
                      Emails = contact.Emails
                      Phones = contact.Phones }
                    :: matches

    List.rev matches, List.rev unknownClubs

/// Prints the report. Contact details are shown but never written to disk - the output of
/// this module is personal data and has no business in the repository.
let printReport (matches: ParentMatch list) (unknownClubs: string list) =
    if matches.IsEmpty then
        printfn "Ingen dommerforeldre funnet."
    else
        let contactOf m =
            match [ yield! m.Phones; yield! m.Emails ] with
            | [] -> "(ingen kontaktinfo)"
            | parts -> String.Join("  ", parts)

        // sized to the data: Norwegian names run long and fixed widths shunt the columns
        let width header pick =
            matches |> List.map (pick >> String.length) |> List.max |> max (String.length header)

        let w1 = width "Utøver" (fun m -> m.Swimmer)
        let w2 = width "Forelder (dommer)" (fun m -> m.Parent)
        let w3 = width "Klubb" (fun m -> m.Club)
        let w4 = width "Dommertype" (fun m -> m.JudgeType)

        let row a b c d e =
            printfn "%-*s  %-*s  %-*s  %-*s  %s" w1 a w2 b w3 c w4 d e

        row "Utøver" "Forelder (dommer)" "Klubb" "Dommertype" "Kontakt"
        printfn "%s" (String.replicate (w1 + w2 + w3 + w4 + 8 + 30) "-")

        for m in matches do
            row m.Swimmer m.Parent m.Club m.JudgeType (contactOf m)

        printfn "\n%d treff." matches.Length

    for club in unknownClubs do
        printfn "Advarsel: klubben '%s' finnes ikke i medleys klubbliste - ingen dommere hentet." club
