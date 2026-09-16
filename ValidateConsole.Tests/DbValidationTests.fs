module DbValidationTests

open Xunit
open DbValidation

/// A direct final as Victoria actually stores it: Runde=0 plus the Direktefinale flag.
let private directFinal =
    { MeetNumber = 1
      EventNumber = 13
      Description = "Øvelse 13. 12*50m fri, mixed"
      Round = Undefined
      PreliminaryEvent = None
      TypeOfFinal = 1
      DirectFinal = true
      Free = false }

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
let ``Runde=Finale without a preliminary event is rejected`` () =
    let e = { directFinal with Round = Final; PreliminaryEvent = None }

    match finalRoundShouldOnlyBeOnFinalEvents e with
    | Ok _ -> failwith "expected an error when Runde=Finale has no forsøksøvelse"
    | Error msg -> Assert.Equal("13. Når runde = Finale, må forsøksøvelse spesifiseres", msg)

[<Fact>]
let ``Runde=Finale with a preliminary event is accepted`` () =
    let e = { directFinal with Round = Final; PreliminaryEvent = Some 3 }

    match finalRoundShouldOnlyBeOnFinalEvents e with
    | Ok actual -> Assert.Equal(e, actual)
    | Error msg -> failwith $"expected Ok, got: {msg}"

/// The reason the rule exists: these are the events that were breaking entry on
/// medley.no, and they must stay valid once switched to Direktefinale.
[<Fact>]
let ``a direct final needs no preliminary event`` () =
    match finalRoundShouldOnlyBeOnFinalEvents directFinal with
    | Ok _ -> ()
    | Error msg -> failwith $"direct final should be valid, got: {msg}"

[<Theory>]
[<InlineData(0)>]
[<InlineData(1)>]
[<InlineData(2)>]
[<InlineData(4)>]
let ``rounds other than Finale are not required to name a preliminary event`` (runde: int) =
    let e = { directFinal with Round = Round.ofInt runde; PreliminaryEvent = None }

    match finalRoundShouldOnlyBeOnFinalEvents e with
    | Ok _ -> ()
    | Error msg -> failwith $"Runde={runde} should not be checked, got: {msg}"
