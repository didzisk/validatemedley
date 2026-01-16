module Validators

let FinalsShouldCostZero () = 
    ()

// Eldste årsklasse på den øvelsen stod til 2007, men det skal være 2006.
// Personen som Niksa forsøkte å melde på var sannsynligvis født i 2006.
// Denne årsklassen glipper dermed mellom eldste årsklasse for junior og seniorklassen.
let EventShouldHaveCorrectAges () = ()

// Jeg ser at både øvelse 26 og øvelse 27 er satt opp med Runde: «Finale»
// Da får man ikke meldt på via påmeldingsløsningen på medley.no fordi denne parameteren forteller at øvelsen vil få påmeldinger under stevnet etter kvalifisering fra andre øvelser.
// Jeg har endret til «Direkte finale» på disse to øvelsene nå, og da skal de være tilgjengelige for påmelding.
let OnlyFinalsShouldHaveRoundFinal () = ()

let MeetShouldHaveExpectedId () = ()

let DivisionsShouldHaveCorrectDates () = ()

let EventsShouldHaveCorrectDates () = ()

