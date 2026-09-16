module DbValidationTests

open Xunit
open DbValidation

/// Meet 18, RA SUPER-SPRINT CUP 2026, as Stevnet holds it.
let private meet =
    { Number = 18
      Name = "RA SUPER-SPRINT CUP 2026"
      Date = "22. aug"
      WomenJunior = 2017
      WomenSenior = 2007
      MenJunior = 2017
      MenSenior = 2007 }

/// Event 3 of that meet: an individual direct final with the meet-wide age span.
let private validEvent =
    { MeetNumber = 18
      EventNumber = 3
      Description = "Øvelse 3. 50m butterfly, damer"
      Round = DirectFinal
      Sex = Female
      Youngest = Some 2017
      Oldest = Some 2008
      DistanceNumber = 5
      IsRelay = Some false
      SessionId = 1
      PreliminaryEvent = None
      TypeOfFinal = 1
      DirectFinal = true
      Free = false }

let private expectOk rule e =
    match rule e with
    | Ok _ -> ()
    | Error msg -> failwith $"expected Ok, got: {msg}"

let private expectError rule e =
    match rule e with
    | Ok _ -> failwith "expected an error"
    | Error msg -> msg

// ---- Runde mapping ----

[<Fact>]
let ``Runde maps to the enum order found in victoria.exe`` () =
    Assert.Equal(Undefined, Round.ofInt 0)
    Assert.Equal(DirectFinal, Round.ofInt 1)
    Assert.Equal(Preliminary, Round.ofInt 2)
    Assert.Equal(Final, Round.ofInt 3)
    Assert.Equal(SemiFinal, Round.ofInt 4)
    Assert.Equal(ThirtySecondFinal, Round.ofInt 8)

[<Fact>]
let ``an unpredicted Runde value does not collapse into Undefined`` () =
    Assert.Equal(UnknownRound 99, Round.ofInt 99)
    Assert.NotEqual(Undefined, Round.ofInt 99)

[<Fact>]
let ``Damerherrer maps to the values Victoria stores`` () =
    Assert.Equal(Female, Sex.ofInt -1)
    Assert.Equal(Male, Sex.ofInt 0)
    Assert.Equal(Mixed, Sex.ofInt -2)
    Assert.Equal(UnknownSex 7, Sex.ofInt 7)

// ---- finalRoundShouldOnlyBeOnFinalEvents ----

[<Fact>]
let ``Runde=Finale without a preliminary event is rejected`` () =
    let e = { validEvent with Round = Final; PreliminaryEvent = None }

    Assert.Equal(
        "3. Når runde = Finale, må forsøksøvelse spesifiseres",
        expectError finalRoundShouldOnlyBeOnFinalEvents e
    )

[<Fact>]
let ``Runde=Finale with a preliminary event is accepted`` () =
    { validEvent with Round = Final; PreliminaryEvent = Some 3 }
    |> expectOk finalRoundShouldOnlyBeOnFinalEvents

[<Fact>]
let ``a direct final needs no preliminary event`` () =
    validEvent |> expectOk finalRoundShouldOnlyBeOnFinalEvents

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(2)>]
[<InlineData(4)>]
let ``rounds other than Finale are not required to name a preliminary event`` (runde: int) =
    { validEvent with Round = Round.ofInt runde; PreliminaryEvent = None }
    |> expectOk finalRoundShouldOnlyBeOnFinalEvents

// ---- seniorLimitShouldBeTheSameAsForMeet ----

let private seniorLimit = seniorLimitShouldBeTheSameAsForMeet meet None

[<Fact>]
let ``an individual event on the meet-wide age span is accepted`` () =
    validEvent |> expectOk seniorLimit

[<Fact>]
let ``youngest other than the meet junior year is rejected`` () =
    Assert.Equal(
        "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)",
        expectError seniorLimit { validEvent with Youngest = Some 2013 }
    )

[<Fact>]
let ``oldest must be one year under the senior year`` () =
    Assert.Equal(
        "Ikke Finale, Eldst må være 1 år under stevnets senioralder (gjelder ikke stafett)",
        expectError seniorLimit { validEvent with Oldest = Some 2014 }
    )

[<Fact>]
let ``a missing age class is rejected rather than treated as unset`` () =
    Assert.Equal(
        "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)",
        expectError seniorLimit { validEvent with Youngest = None }
    )

[<Fact>]
let ``men's events are checked against the men's years`` () =
    let meetWithDifferentMensAges = { meet with MenJunior = 2015; MenSenior = 2005 }
    let rule = seniorLimitShouldBeTheSameAsForMeet meetWithDifferentMensAges None
    let male = { validEvent with Sex = Male; Youngest = Some 2015; Oldest = Some 2006 }

    male |> expectOk rule
    // the women's span must now fail, proving the branch is actually selected by Sex
    Assert.Equal(
        "Ikke Finale, Yngst må være som i hele stevnet (gjelder ikke stafett)",
        expectError rule { male with Youngest = Some 2017 }
    )

/// Mirrors ValidationMain: Mixed is not special-cased, so it lands on the men's years.
[<Fact>]
let ``mixed events are checked against the men's years`` () =
    let meetWithDifferentMensAges = { meet with MenJunior = 2015; MenSenior = 2005 }
    let rule = seniorLimitShouldBeTheSameAsForMeet meetWithDifferentMensAges None

    { validEvent with Sex = Mixed; Youngest = Some 2015; Oldest = Some 2006 }
    |> expectOk rule

[<Fact>]
let ``relays are exempt from the age span`` () =
    { validEvent with
        Sex = Mixed
        IsRelay = Some true
        Youngest = None
        Oldest = None }
    |> expectOk seniorLimit

[<Fact>]
let ``finals are exempt from the age span`` () =
    { validEvent with
        Round = Final
        Youngest = Some 2013
        Oldest = Some 2014 }
    |> expectOk seniorLimit

/// A Distansenr with no row in Distanser must surface, not silently validate as individual.
[<Fact>]
let ``an unresolvable distance is reported`` () =
    Assert.Equal(
        "3. Ukjent distanse (Distansenr 36), kan ikke avgjøre om øvelsen er stafett",
        expectError seniorLimit { validEvent with DistanceNumber = 36; IsRelay = None }
    )

// ---- age-split meets (meets.json) ----

/// Meet 18 as configured: young band is session 1, cutoff 2014.
/// Bands are then [2017..2014] and [2013..2008].
let private split: MeetConfig.MeetSplit =
    { CutoffYear = 2014
      YoungSessions = Set.ofList [ 1 ] }

let private splitLimit = seniorLimitShouldBeTheSameAsForMeet meet (Some split)

[<Fact>]
let ``young band keeps the meet junior year and ends at the cutoff`` () =
    { validEvent with SessionId = 1; Youngest = Some 2017; Oldest = Some 2014 }
    |> expectOk splitLimit

[<Fact>]
let ``older band starts below the cutoff and ends at the senior year`` () =
    { validEvent with SessionId = 2; Youngest = Some 2013; Oldest = Some 2008 }
    |> expectOk splitLimit

/// The exact span that the unsplit rule demands must now fail, or the split is not applied.
[<Fact>]
let ``the meet-wide span is rejected in an age-split meet`` () =
    Assert.Equal(
        "Ikke Finale, Eldst må være 2014 i yngste årsgruppe (aldersdelt stevne)",
        expectError splitLimit { validEvent with SessionId = 1; Youngest = Some 2017; Oldest = Some 2008 }
    )

[<Fact>]
let ``an older-band event scheduled into the young session is rejected`` () =
    Assert.Equal(
        "Ikke Finale, Yngst må være 2017 i yngste årsgruppe (aldersdelt stevne)",
        expectError splitLimit { validEvent with SessionId = 1; Youngest = Some 2013; Oldest = Some 2008 }
    )

[<Fact>]
let ``a young-band event scheduled into the older session is rejected`` () =
    Assert.Equal(
        "Ikke Finale, Yngst må være 2013 i eldste årsgruppe (aldersdelt stevne)",
        expectError splitLimit { validEvent with SessionId = 2; Youngest = Some 2017; Oldest = Some 2014 }
    )

/// A two-day meet puts a young session on each day.
[<Fact>]
let ``several sessions can make up the young band`` () =
    let twoDay: MeetConfig.MeetSplit =
        { CutoffYear = 2014; YoungSessions = Set.ofList [ 1; 3 ] }

    let rule = seniorLimitShouldBeTheSameAsForMeet meet (Some twoDay)

    { validEvent with SessionId = 3; Youngest = Some 2017; Oldest = Some 2014 } |> expectOk rule
    { validEvent with SessionId = 4; Youngest = Some 2013; Oldest = Some 2008 } |> expectOk rule

[<Fact>]
let ``relays and finals stay exempt in an age-split meet`` () =
    { validEvent with SessionId = 2; IsRelay = Some true; Youngest = None; Oldest = None }
    |> expectOk splitLimit

    { validEvent with SessionId = 2; Round = Final; Youngest = Some 2017; Oldest = Some 2014 }
    |> expectOk splitLimit

// ---- finalRoundShouldNotHaveYoungestEldest ----

/// A final as Victoria stores it: age class -1/-1, which reads as None.
let private final =
    { validEvent with
        Round = Final
        PreliminaryEvent = Some 3
        Youngest = None
        Oldest = None
        Free = true }

[<Fact>]
let ``a final carrying no age class is accepted`` () =
    final |> expectOk finalRoundShouldNotHaveYoungestEldest

[<Fact>]
let ``a final may not set youngest`` () =
    Assert.Equal(
        "3. Finale, Yngst er ikke tillatt",
        expectError finalRoundShouldNotHaveYoungestEldest { final with Youngest = Some 2017 }
    )

[<Fact>]
let ``a final may not set oldest`` () =
    Assert.Equal(
        "3. Finale, Eldst er ikke tillatt",
        expectError finalRoundShouldNotHaveYoungestEldest { final with Oldest = Some 2008 }
    )

[<Fact>]
let ``non-finals keep their age class`` () =
    validEvent |> expectOk finalRoundShouldNotHaveYoungestEldest

// ---- finalAandBShouldHaveDifferentContents ----

[<Fact>]
let ``an A and a B final off the same preliminary event are accepted`` () =
    let a = { final with EventNumber = 15; TypeOfFinal = 1 }
    let b = { final with EventNumber = 13; TypeOfFinal = 2 }
    let events = [ a; b ]

    a |> expectOk (finalAandBShouldHaveDifferentContents events)
    b |> expectOk (finalAandBShouldHaveDifferentContents events)

[<Fact>]
let ``two finals with the same preliminary event and type are rejected`` () =
    let a = { final with EventNumber = 15; TypeOfFinal = 1 }
    let duplicate = { final with EventNumber = 16; TypeOfFinal = 1 }

    Assert.Equal(
        "A og B finale skal ikke ha samme innhold",
        expectError (finalAandBShouldHaveDifferentContents [ a; duplicate ]) a
    )

[<Fact>]
let ``finals off different preliminary events may share a type`` () =
    let a = { final with EventNumber = 15; PreliminaryEvent = Some 3; TypeOfFinal = 1 }
    let b = { final with EventNumber = 16; PreliminaryEvent = Some 4; TypeOfFinal = 1 }

    a |> expectOk (finalAandBShouldHaveDifferentContents [ a; b ])

[<Fact>]
let ``duplicate non-finals are not this rule's business`` () =
    let a = validEvent
    let b = { validEvent with EventNumber = 4 }

    a |> expectOk (finalAandBShouldHaveDifferentContents [ a; b ])

// ---- finalShouldBeFree ----

[<Fact>]
let ``a free final is accepted`` () =
    final |> expectOk finalShouldBeFree

[<Fact>]
let ``a final that costs money is rejected`` () =
    Assert.Equal(
        "Finale, bør være gratis (Generelt 2 | Øvelsen skal ikke betales for)",
        expectError finalShouldBeFree { final with Free = false }
    )

[<Fact>]
let ``non-finals are allowed to cost money`` () =
    validEvent |> expectOk finalShouldBeFree

// ---- config parsing ----

[<Fact>]
let ``meets json parses into a lookup keyed by stevnenr`` () =
    let json =
        """{ "meets": [ { "stevnenr": 18, "name": "RA SUPER-SPRINT CUP 2026",
                          "cutoffYear": 2014, "youngSessions": [ 1 ] },
                        { "stevnenr": 20, "name": "To dager",
                          "cutoffYear": 2015, "youngSessions": [ 1, 3 ] } ] }"""

    let splits = MeetConfig.parse json

    Assert.Equal(2, splits.Count)
    Assert.Equal({ MeetConfig.MeetSplit.CutoffYear = 2014; MeetConfig.MeetSplit.YoungSessions = Set.ofList [ 1 ] }, splits.[18])
    Assert.Equal({ MeetConfig.MeetSplit.CutoffYear = 2015; MeetConfig.MeetSplit.YoungSessions = Set.ofList [ 1; 3 ] }, splits.[20])

[<Fact>]
let ``a missing config file means no meet is age-split`` () =
    Assert.Empty(MeetConfig.load "no-such-file.json")
