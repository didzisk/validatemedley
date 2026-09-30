# Validate

Validates swim meet setups for Victoria, a 32-bit meet-management program that stores its
data in Access databases. Two validation paths exist over the same rules:

- **XML** (`StevneXml`, `ValidationMain`) - the meet setup as victoria.exe exports it.
- **Database** (`AccessDb*`, `DbValidation`) - read straight from `stevne.mdb`.

## Commands

```
dotnet build Validate.sln
dotnet test --project ValidateConsole.Tests\ValidateConsole.Tests.fsproj
dotnet run --project ValidateConsole
```

`dotnet test` runs on Microsoft.Testing.Platform (xunit v3). The opt-in is the `test` section
of `global.json`; without it `dotnet test` starts VSTest, finds no adapter and **hangs**.
`--nologo` is rejected in this mode (exit code 5). The test project also builds to a runnable
exe, so `ValidateConsole.Tests\bin\Debug\net10.0\win-x86\ValidateConsole.Tests.exe` works too.

## The project must stay x86

The databases are **Jet 3** (Access 97 - header byte `0x14` is `0x00`). Every ACE provider
rejects that format with *"Cannot open a database created with a previous version of your
application"*. Only `Microsoft.Jet.OLEDB.4.0` reads it, and that provider exists only as
32-bit. So:

- Both projects set `RuntimeIdentifier=win-x86`, `SelfContained=false`.
- The **x86** .NET 10 runtime must be installed (`winget install Microsoft.DotNet.Runtime.10 --architecture x86`).
  The x64 SDK cross-builds it fine; only running needs the x86 runtime.
- victoria.exe is itself x86, for the same reason.

Do not "clean up" the RuntimeIdentifier, and do not add ACE providers back as a fallback -
they cannot ever succeed on these files. An `.accdb` would need ACE, but there are none.

Databases live in `D:\training\StevneMedley\DbCopy`: `person.mdb` (clubs, countries),
`rekorder.mdb` (records), `stevne.mdb` (meets). Opened with `Mode=Read`, which Jet honours -
no `.ldb` lock files appear.

## Modules (compile order matters in F#)

| File | Responsibility |
|---|---|
| `AccessDb.fs` | Connection lifetime, `query`, `scalar`. Nothing else. |
| `AccessDbSchema.fs` | `listTables`, `columns`, `rowCount`, `describeTable`. |
| `AccessDbDump.fs` | `render`, `readRows`, `dumpDatabase` - text output only. |
| `MeetConfig.fs` | Loads `meets.json`. |
| `DbValidation.fs` | Row types and the validation rules. |

Build typed readers on `AccessDb.query conn sql mapRow`. Keep presentation out of
`AccessDb`/`AccessDbSchema`.

`MeetConfig` parses with `JsonValue`, not `JsonProvider` - the provider fails to generate for
this shape on the current FSharp.Core (*"Expecting delegate type"*). `XmlProvider` in
`StevneXml` is fine.

## Victoria's schema, decoded

None of this is documented anywhere else; it was derived from the data and from strings inside
victoria.exe.

**`Stevneoppsett`** - one row per event, **for every meet in the file**. `Stevnedistansenr` is
the event number and is only unique within a `Stevnenr`, so *always* filter by meet.

- `Runde` indexes the enum table found in victoria.exe:
  `0` UNDEFINED, `1` DIRECTFINAL, `2` PRELIMINARY, `3` FINAL, `4` SEMIFINAL,
  `5` QUARTERFINAL, `6` 8FINAL, `7` 16FINAL, `8` 32FINAL.
  `0` and `1` are confirmed from data; `3` is inferred but corroborated by `Ovelsestekst`.
- `Damerherrer`: `-1` FEMALE, `0` MALE, `-2` MIXED. (Not 0/1/2 - it is a tri-state control.)
- `Yngsteklasse` / `Eldsteklasse`: birth years held as **text**, `-1` when unset (relays).
- `Forsoksovelse` = preliminary event number, `0` for none. `Finaletype` = A/B final.
- Booleans are inconsistent: `Direktefinale` is `1`, `Senior` is `-1`. Treat non-zero as true.

**`Distanser`** - the shared distance catalogue. `Lagind` is `-1` for relays, `0` otherwise.
Use it to detect relays; the XML rule's `EventLength.Contains("*")` test is wrong, because
`Distansenr 37` is a relay whose length reads `300`.

**`Stevnet`** - one row per meet, 133 columns. The age columns are `Damerjunior`,
`Damersenior`, `Herrerjunior`, `Herrersenior` (the XML's `WomenJunior` etc.).

**`Session`** - **reserved word in Jet, must be bracketed as `[Session]`** or the query fails
with an unhelpful `E_FAIL`.

## Rules

`DbValidation` mirrors `ValidationMain`, in the same pipeline order and with the same messages
so the two paths stay comparable:

1. `finalRoundShouldOnlyBeOnFinalEvents`
2. `finalRoundShouldNotHaveYoungestEldest`
3. `seniorLimitShouldBeTheSameAsForMeet`
4. `finalAandBShouldHaveDifferentContents`
5. `finalShouldBeFree`

`Validators.fs` still holds six empty stubs that were never implemented on either path.

### Age-split meets (`meets.json`)

Some meets split swimmers into age bands across sessions - young in one, older in another -
and Victoria's `Session` table has no age fields to record it. The cutoff therefore lives in
`ValidateConsole\meets.json`, keyed by `Stevnenr`:

```json
{ "meets": [ { "stevnenr": 18, "name": "...", "cutoffYear": 2014, "youngSessions": [ 1 ] } ] }
```

With cutoff `C`, the young band is `[meet junior .. C]` and the older band
`[C-1 .. meet senior + 1]`; both ends must match exactly. `youngSessions` is a list because a
two-day meet has a young session on each day. Meets absent from the file keep the plain
meet-wide age check, so adding a meet only affects that meet.

Relays and finals are exempt from the age rules either way.

## Conventions

- Write files with the Write/Edit tools, never by rewriting them from PowerShell - .NET file
  writes there trigger an elevation prompt. PowerShell is for builds, tests and queries.
- Line endings sort themselves out: `.gitattributes` has `* text=auto` and `core.autocrlf` is
  true, so the repo stores LF and checks out CRLF. The "LF will be replaced by CRLF" message is
  `core.safecrlf` narrating a correct conversion; silence it with
  `git config core.safecrlf false` rather than by rewriting files.
