/// Age-band configuration for meets whose events are split across sessions by birth year.
/// Victoria's Session table has no age fields, so the cutoff cannot be read from the
/// database and is configured in meets.json instead.
module MeetConfig

open System
open System.IO
open FSharp.Data
open FSharp.Data.JsonExtensions

/// CutoffYear is the oldest birth year still in the young band, so the young band runs
/// [meet junior .. CutoffYear] and the older band [CutoffYear - 1 .. meet senior + 1].
type MeetSplit =
    { CutoffYear: int
      YoungSessions: Set<int> }

let [<Literal>] FileName = "meets.json"

/// Path of the config shipped next to the executable.
let defaultPath () = Path.Combine(AppContext.BaseDirectory, FileName)

/// Parsed with JsonValue rather than JsonProvider: the provider fails to generate for this
/// shape on the current FSharp.Core ("Expecting delegate type").
let parse (json: string) : Map<int, MeetSplit> =
    let root = JsonValue.Parse json

    match root.TryGetProperty "meets" with
    | None -> Map.empty
    | Some meets ->
        meets.AsArray()
        |> Array.map (fun m ->
            m?stevnenr.AsInteger(),
            { CutoffYear = m?cutoffYear.AsInteger()
              YoungSessions =
                m?youngSessions.AsArray()
                |> Array.map (fun s -> s.AsInteger())
                |> Set.ofArray })
        |> Map.ofArray

/// Missing config is not an error - it just means no meet is age-split.
let load (path: string) : Map<int, MeetSplit> =
    if File.Exists path then parse (File.ReadAllText path) else Map.empty
