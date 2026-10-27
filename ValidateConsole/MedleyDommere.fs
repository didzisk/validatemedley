/// Reads the national register of swim officials from medley.no/dommeroversikt.aspx.
///
/// Victoria's own databases cannot answer this: person.mdb has Dommer/Dommertype columns but
/// they are empty in a club installation, and stevne.mdb's Dommer table has no rows. The
/// register only exists centrally.
///
/// The page is ASP.NET WebForms hosting a DevExpress ASPxGridView, and none of the protocol
/// below is documented - it was read off the wire. Two facts drive the whole design:
///
///   * The grid binds its data **only on the first GET**. Any ordinary postback, even one
///     carrying nothing but the ASP.NET hidden fields, comes back with "Ingen data a vise".
///     Everything therefore goes through DevExpress callbacks.
///   * Callbacks are stateful. A callback must echo back the row keys and the grid's
///     callbackState from the response that preceded it, or the server answers with
///     'generalError'.
///
/// Keeping all of that here means a change at medley.no breaks one file.
module MedleyDommere

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.RegularExpressions

/// One row of the dommer grid. Key is DevExpress's row key, "<gren>_<personid>", and is how
/// a row is addressed when expanding it - the visible index is not stable across callbacks.
type Official =
    { Key: string
      Club: string
      FirstName: string
      LastName: string
      JudgeType: string }

/// What the expand panel discloses. Both lists are empty when the official registered neither.
type Contact = { Emails: string list; Phones: string list }

let [<Literal>] Url = "https://medley.no/dommeroversikt.aspx"

/// The grid's UniqueID, which is also the callback target.
let [<Literal>] GridId = "ctl00$ctl00$Content$MainContent$grdDommere"

// ---------------------------------------------------------------- wire protocol

/// Rebinds the grid to whatever the cmb*_VI fields currently say. This is what the club
/// combo's SelectedIndexChanged handler raises via grdDommere.PerformCallback().
///
/// The leading number identifies the control that raised the callback and differs per
/// control - 14 for the combo-driven rebind, 13 for a detail button, 12 for the bottom pager.
let [<Literal>] rebindCommand = "14|CUSTOMCALLBACK0|"

/// Expands one row's detail panel, addressed by row key.
let expandCommand (key: string) = $"13|SHOWDETAILROW7|{key}"

/// Builds __CALLBACKPARAM. Each segment is NAME|<character count>;<value>; and the server
/// validates those counts, so they are computed rather than guessed.
///   KV - the keys of the rows currently bound, as a JSON array
///   FR - focused row
///   GB - the command itself
let callbackParam (keys: string list) (command: string) =
    let kv = "[" + String.Join(",", keys |> List.map (fun k -> "\"" + k + "\"")) + "]"
    $"c0:KV|{kv.Length};{kv};FR|1;0;GB|{command.Length};{command};"

/// A callback answers with zero or more length-prefixed hidden-field values (the first is the
/// refreshed __EVENTVALIDATION) followed by the DX payload, whose HTML is JS-escaped.
let splitResponse (raw: string) =
    let rec take (pos: int) acc =
        let bar = raw.IndexOf('|', pos)

        if bar < 0 then
            acc, pos
        else
            match Int32.TryParse(raw.Substring(pos, bar - pos)) with
            | true, n when bar + 1 + n <= raw.Length -> take (bar + 1 + n) (raw.Substring(bar + 1, n) :: acc)
            | _ -> acc, pos

    let hidden, pos = take 0 []

    let payload =
        raw
            .Substring(pos)
            .Replace("\\r\\n", "\n")
            .Replace("\\'", "'")
            .Replace("\\\"", "\"")
            .Replace("\\/", "/")

    List.rev hidden, payload

/// DevExpress reports a server-side failure inside the payload with HTTP 200, so an
/// unchecked caller would read it as "no officials in this club".
let isCallbackError (payload: string) = payload.Contains "'generalError'"

// ---------------------------------------------------------------- page scraping

let private decode (s: string) = WebUtility.HtmlDecode(s).Trim()

/// Club name -> combo value. Read from the page because Victoria's Klubbnr is a different
/// numbering altogether (Kolbotn IL is 75 in person.mdb but 73 here), so names are the only
/// shared key.
///
/// All three combos on the page render the same {'value':..,'texts':['..']} shape, so the
/// search is anchored on the club listbox's own id.
let parseClubIds (html: string) =
    let anchor = html.IndexOf "cmbKlubber$DDD$L"

    if anchor < 0 then
        Map.empty
    else
        let itemsAt = html.IndexOf("'itemsInfo':[", anchor)

        if itemsAt < 0 then
            Map.empty
        else
            let close = html.IndexOf("}]", itemsAt)
            let segment = html.Substring(itemsAt, (if close < 0 then html.Length else close + 2) - itemsAt)

            Regex.Matches(segment, @"\{'value':(\d+),'texts':\['([^']*)'\]\}")
            |> Seq.map (fun m -> decode m.Groups.[2].Value, int m.Groups.[1].Value)
            |> Map.ofSeq

/// Row keys, in the same order as the rendered rows.
let parseKeys (html: string) =
    let m = Regex.Match(html, @"'keys':\[([^\]]*)\]")

    if not m.Success then
        []
    else
        Regex.Matches(m.Groups.[1].Value, @"'([^']+)'")
        |> Seq.map (fun g -> g.Groups.[1].Value)
        |> List.ofSeq

/// The grid's opaque state. It must be echoed back on the next callback.
let parseCallbackState (html: string) =
    Regex.Match(html, @"'callbackState':'([^']*)'").Groups.[1].Value

/// Grid rows, paired with the keys of the same bind. The first cell of a row holds the expand
/// image and no text, so the text cells begin at Klubb; the checkbox columns hold a span and
/// are skipped the same way.
let parseOfficials (keys: string list) (html: string) =
    let keys = List.toArray keys

    Regex.Matches(html, @"DXDataRow(\d+)""[\s\S]{0,6000}?</tr>")
    |> Seq.choose (fun m ->
        let index = int m.Groups.[1].Value

        let cells =
            Regex.Matches(m.Value, @"<td class=""[^""]*dxgv[^""]*""[^>]*>([^<]*)</td>")
            |> Seq.map (fun c -> decode c.Groups.[1].Value)
            |> Array.ofSeq

        if cells.Length < 5 || index >= keys.Length then
            None
        else
            Some
                { Key = keys.[index]
                  Club = cells.[0]
                  FirstName = cells.[1]
                  LastName = cells.[2]
                  JudgeType = cells.[4] })
    |> List.ofSeq

/// The detail panel. Element ids end in the row's visible index, which differs per row, so
/// every pattern here ends at \d+ rather than a fixed suffix. One Epost cell can hold several
/// comma-separated addresses.
let parseContact (payload: string) =
    let emails =
        Regex.Matches(payload, @"href=""mailto:([^""]+)""")
        |> Seq.collect (fun m -> m.Groups.[1].Value.Split(','))
        |> Seq.map decode
        |> Seq.filter (fun e -> e <> "")
        |> Seq.distinct
        |> List.ofSeq

    let phones =
        Regex.Matches(payload, @"_lbl(?:Mobil|Telefon)\d*_\d+""[^>]*>([^<]+)<")
        |> Seq.map (fun m -> decode m.Groups.[1].Value)
        |> Seq.filter (fun p -> p <> "")
        |> Seq.distinct
        |> List.ofSeq

    { Emails = emails; Phones = phones }

// ---------------------------------------------------------------- session

/// Everything a callback has to echo back. Fields carries the page's hidden inputs plus the
/// combo values; Keys and State come from whichever bind happened last.
type private Session =
    { Client: HttpClient
      Fields: Collections.Generic.Dictionary<string, string>
      Keys: string list
      State: string }

let private newClient () =
    let handler = new HttpClientHandler(CookieContainer = CookieContainer(), UseCookies = true)
    let client = new HttpClient(handler)
    client.DefaultRequestHeaders.Add("User-Agent", "StevneMedley-Validate (+local club tooling)")
    client.DefaultRequestHeaders.Add("Referer", Url)
    client

let private get (client: HttpClient) =
    client.GetStringAsync(Url) |> Async.AwaitTask |> Async.RunSynchronously

let private hiddenInputs (html: string) =
    let fields = Collections.Generic.Dictionary<string, string>()

    for m in Regex.Matches(html, "<input[^>]*>") do
        let name = Regex.Match(m.Value, @"name=""([^""]+)""").Groups.[1].Value
        let value = Regex.Match(m.Value, @"value=""([^""]*)""").Groups.[1].Value

        if name <> "" then
            fields.[name] <- WebUtility.HtmlDecode value

    // submit buttons must not be posted, or the server treats the callback as a login attempt
    fields.Remove "ctl00$ctl00$btnLoggInn" |> ignore
    fields.Remove "ctl00$ctl00$btnLoggUt" |> ignore
    fields

/// Issues one callback and folds the refreshed state back into the session.
let private callback (session: Session) (command: string) =
    let fields = session.Fields
    fields.[GridId] <- $"""{{"focusedRow":0,"selection":"","callbackState":"{session.State}"}}"""
    fields.["__CALLBACKID"] <- GridId
    fields.["__CALLBACKPARAM"] <- callbackParam session.Keys command
    fields.["__EVENTTARGET"] <- ""
    fields.["__EVENTARGUMENT"] <- ""

    use content = new FormUrlEncodedContent(fields)

    let raw =
        task {
            let! response = session.Client.PostAsync(Url, content)
            response.EnsureSuccessStatusCode() |> ignore
            return! response.Content.ReadAsStringAsync()
        }
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let hidden, payload = splitResponse raw

    if isCallbackError payload then
        failwithf
            "medley.no rejected the callback '%s'. The page's DevExpress protocol has probably changed."
            command

    match hidden with
    | first :: _ -> fields.["__EVENTVALIDATION"] <- first
    | [] -> ()

    payload,
    { session with
        Keys = parseKeys payload
        State = parseCallbackState payload }

/// Opens the page and selects a club, leaving the session bound to that club's officials.
let private openForClub (clubId: int) =
    let client = newClient ()
    let html = get client
    let fields = hiddenInputs html
    fields.["cmbKretser_VI"] <- "0"
    fields.["cmbGren_VI"] <- "0"
    fields.["cmbKlubber_VI"] <- string clubId

    let session =
        { Client = client
          Fields = fields
          Keys = parseKeys html
          State = parseCallbackState html }

    let payload, session = callback session rebindCommand
    payload, session

/// Club name -> combo id, as the page lists them.
let clubIds () =
    use client = newClient ()
    get client |> parseClubIds

/// The officials registered to one club. Filtering by club avoids the pager entirely: the
/// unfiltered grid is 1198 rows over 12 pages, a single club fits on one.
let officialsForClub (clubId: int) =
    let payload, session = openForClub clubId

    try
        parseOfficials session.Keys payload
    finally
        session.Client.Dispose()

/// Officials of one club together with the contact details behind each expand button.
/// `wanted` selects which rows are worth a round trip - expanding every official of every
/// club would be hundreds of requests for data nobody asked for.
let officialsWithContacts (clubId: int) (wanted: Official -> bool) =
    let payload, session = openForClub clubId

    try
        let officials = parseOfficials session.Keys payload

        let mutable current = session

        [ for official in officials do
              if wanted official then
                  let detail, next = callback current (expandCommand official.Key)
                  current <- next
                  yield official, parseContact detail
              else
                  yield official, { Emails = []; Phones = [] } ]
    finally
        session.Client.Dispose()
