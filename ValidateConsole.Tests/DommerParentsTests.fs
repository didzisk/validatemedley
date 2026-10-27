module DommerParentsTests

open Xunit
open DommerParents

let private official first last club : MedleyDommere.Official =
    { Key = "1_1"
      Club = club
      FirstName = first
      LastName = last
      JudgeType = "Kretsdommer" }

// ---- surname tokens ----

[<Fact>]
let ``a simple surname is one token`` () =
    Assert.Equal<Set<string>>(Set.ofList [ "klavins" ], surnameTokens "Klavins")

[<Fact>]
let ``a compound surname splits on whitespace`` () =
    Assert.Equal<Set<string>>(Set.ofList [ "strøm"; "fredriksen" ], surnameTokens "Strøm Fredriksen")

[<Fact>]
let ``a hyphenated surname splits on the hyphen`` () =
    Assert.Equal<Set<string>>(Set.ofList [ "berg"; "hansen" ], surnameTokens "Berg-Hansen")

[<Fact>]
let ``tokens ignore case and surrounding space`` () =
    Assert.Equal<Set<string>>(Set.ofList [ "klavins" ], surnameTokens "  KLAVINS ")

// ---- parent candidates ----

[<Fact>]
let ``an official sharing the swimmer's surname is a candidate`` () =
    Assert.True(isParent "Marta" "Klavins" (official "Didzis" "Klavins" "Kolbotn IL"))

[<Fact>]
let ``a shared part of a compound surname is enough`` () =
    Assert.True(isParent "Sara Strøm" "Fredriksen" (official "Åsmund" "Strøm Fredriksen" "Alta SK"))
    Assert.True(isParent "Sara" "Strøm Fredriksen" (official "Åsmund" "Fredriksen" "Alta SK"))

[<Fact>]
let ``an unrelated surname is not a candidate`` () =
    Assert.False(isParent "Marta" "Klavins" (official "Rune" "Gran" "Kolbotn IL"))

[<Fact>]
let ``the swimmer is not their own parent`` () =
    Assert.False(isParent "Didzis" "Klavins" (official "Didzis" "Klavins" "Kolbotn IL"))

[<Fact>]
let ``the self check ignores case and spacing`` () =
    Assert.False(isParent " didzis " "KLAVINS" (official "Didzis" "Klavins" "Kolbotn IL"))

[<Fact>]
let ``a sibling with the same surname but a different first name still counts`` () =
    // two children of one official both match; that is intended, the caller de-duplicates
    Assert.True(isParent "Anna" "Klavins" (official "Didzis" "Klavins" "Kolbotn IL"))

// ---- club name matching ----

[<Fact>]
let ``club names are compared case and whitespace insensitively`` () =
    Assert.True(sameClub "Kolbotn IL" " kolbotn il ")
    Assert.False(sameClub "Kolbotn IL" "Ski SK")

// ---- report rows ----

[<Fact>]
let ``matching pairs each swimmer with every official candidate in their club`` () =
    let swimmers =
        [ { Id = 1; FirstName = "Anna"; LastName = "Klavins"; Club = "Kolbotn IL" }
          { Id = 2; FirstName = "Rune"; LastName = "Gran"; Club = "Kolbotn IL" }
          { Id = 3; FirstName = "Lisa"; LastName = "Klavins"; Club = "Ski SK" } ]

    let officials = [ official "Didzis" "Klavins" "Kolbotn IL" ]

    let matches = matchSwimmers swimmers officials

    // Anna matches; Rune's surname differs; Lisa is in another club
    Assert.Equal(1, matches.Length)
    Assert.Equal("Anna Klavins", fst matches.Head)
    Assert.Equal("Didzis", (snd matches.Head).FirstName)
