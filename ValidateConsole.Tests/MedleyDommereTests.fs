module MedleyDommereTests

open Xunit
open MedleyDommere

// ---- callback envelope ----
// Every segment is NAME|<character count of the value>;<value>; - the counts are what the
// server validates, so they are the part worth pinning down.

[<Fact>]
let ``callbackParam prefixes keys and command with their character counts`` () =
    Assert.Equal(
        "c0:KV|13;[\"1_1\",\"1_2\"];FR|1;0;GB|21;13|SHOWDETAILROW7|1_1;",
        callbackParam [ "1_1"; "1_2" ] (expandCommand "1_1")
    )

[<Fact>]
let ``rebinding the grid uses the command the club combo sends`` () =
    Assert.Equal("14|CUSTOMCALLBACK0|", rebindCommand)
    Assert.Equal("c0:KV|2;[];FR|1;0;GB|19;14|CUSTOMCALLBACK0|;", callbackParam [] rebindCommand)

[<Fact>]
let ``expanding a row addresses it by key, not by position`` () =
    Assert.Equal("13|SHOWDETAILROW7|1_45042", expandCommand "1_45042")

// ---- response envelope ----
// A callback answers with length-prefixed hidden fields, then a JS-escaped DX payload.

[<Fact>]
let ``splitResponse separates the hidden fields from the payload`` () =
    let hidden, payload = splitResponse @"5|ABCDE/*DX*/({'result':{'html':'<b>hi</b>'}})"

    Assert.Equal<string list>([ "ABCDE" ], hidden)
    Assert.StartsWith("/*DX*/", payload)

[<Fact>]
let ``splitResponse unescapes the payload back into HTML`` () =
    let _, payload = splitResponse @"2|XY/*DX*/'<a href=\'m\'>a\r\nb<\/a>'"

    Assert.Contains("<a href='m'>", payload)
    Assert.Contains("</a>", payload)
    Assert.Contains("a\nb", payload)

[<Fact>]
let ``a response with no length prefix is all payload`` () =
    let hidden, payload = splitResponse "<html>no callback here</html>"

    Assert.Empty(hidden)
    Assert.Equal("<html>no callback here</html>", payload)

// ---- page scraping ----

/// The club combo's items, with a decoy list before them: the page carries three combos and
/// they all render the same {'value':..,'texts':[..]} shape.
let private clubPage =
    """<script>ASPx.CreateListBox('cmbGren$DDD$L',{'itemsInfo':[{'value':0,'texts':['Vis alle grener']},
       {'value':1,'texts':['Sv&#248;mming']}]},{'SelectedIndexChanged':function (s, e) { x('cmbGren', e); }});
       ASPx.CreateListBox('cmbKlubber$DDD$L',{'scStates':6,'itemsInfo':[{'value':0,'texts':['Vis alle klubber']},
       {'value':1,'texts':['Alta SK']},{'value':73,'texts':['Kolbotn IL']}]},
       {'SelectedIndexChanged':function (s, e) { ASPx.CBLBSelectedIndexChanged('cmbKlubber', e); }});</script>"""

[<Fact>]
let ``parseClubIds reads the club combo, not another combo on the page`` () =
    let clubs = parseClubIds clubPage

    Assert.Equal(73, clubs.["Kolbotn IL"])
    Assert.Equal(1, clubs.["Alta SK"])
    // "Svømming" belongs to the discipline combo and must not leak in
    Assert.False(clubs.ContainsKey "Svømming")

[<Fact>]
let ``parseClubIds decodes html entities in club names`` () =
    let page =
        """ASPx.CreateListBox('cmbKlubber$DDD$L',{'itemsInfo':[{'value':9,'texts':['L&#248;renskog']}]},
           {'SelectedIndexChanged':function (s, e) { ASPx.CBLBSelectedIndexChanged('cmbKlubber', e); }});"""

    Assert.Equal(9, (parseClubIds page).["Lørenskog"])

[<Fact>]
let ``parseKeys reads the row keys in grid order`` () =
    Assert.Equal<string list>(
        [ "1_40674"; "1_45382" ],
        parseKeys "...'keys':['1_40674','1_45382'],'columnCount':11..."
    )

[<Fact>]
let ``parseCallbackState picks up the grid state blob`` () =
    Assert.Equal("abc+/=", parseCallbackState "...'callbackState':'abc+/='},'uniqueID'...")

/// Two grid rows as the server renders them. The first cell holds the expand image and has
/// no text, so the text cells start at Klubb.
let private gridRows =
    """<tr id="grdDommere_DXDataRow0" class="dxgvDataRow_PlasticBlue">
<td class="dxgvDetailButton_PlasticBlue dxgv"><img alt="[Utvid]" /></td><td class="dxgv">Kolbotn IL</td>
<td class="kolonneVis dxgv">Stig Morten</td><td class="kolonneVis dxgv">Bentsen</td>
<td class="kolonneVis2 dxgv">Stig Morten Bentsen</td><td class="kolonneVis dxgv">Kretsdommer</td>
<td class="kolonneVis dxgv">Sv&#248;mming</td><td class="kolonneVis dxgv"><span class="dxWeb"></span></td></tr>
<tr id="grdDommere_DXDataRow1" class="dxgvDataRow_PlasticBlue">
<td class="dxgvDetailButton_PlasticBlue dxgv"><img alt="[Utvid]" /></td><td class="dxgv">Kolbotn IL</td>
<td class="kolonneVis dxgv">Didzis</td><td class="kolonneVis dxgv">Klavins</td>
<td class="kolonneVis2 dxgv">Didzis Klavins</td><td class="kolonneVis dxgv">Kretsdommer</td>
<td class="kolonneVis dxgv">Sv&#248;mming</td><td class="kolonneVis dxgv"><span class="dxWeb"></span></td></tr>"""

[<Fact>]
let ``parseOfficials pairs each row with its key`` () =
    let officials = parseOfficials [ "1_40674"; "1_45042" ] gridRows

    Assert.Equal(2, officials.Length)
    Assert.Equal("1_45042", officials.[1].Key)
    Assert.Equal("Didzis", officials.[1].FirstName)
    Assert.Equal("Klavins", officials.[1].LastName)
    Assert.Equal("Kolbotn IL", officials.[1].Club)
    Assert.Equal("Kretsdommer", officials.[1].JudgeType)

[<Fact>]
let ``parseOfficials decodes html entities in names`` () =
    let row =
        """<tr id="grdDommere_DXDataRow0"><td class="dxgvDetailButton_PlasticBlue dxgv"><img /></td>
<td class="dxgv">Kolbotn IL</td><td class="kolonneVis dxgv">&#216;ystein</td>
<td class="kolonneVis dxgv">Kvisla</td><td class="kolonneVis2 dxgv">&#216;ystein Kvisla</td>
<td class="kolonneVis dxgv">Kretsdommer</td></tr>"""

    Assert.Equal("Øystein", (parseOfficials [ "1_1" ] row).Head.FirstName)

// ---- detail panel ----
// The element id suffix is the row's visible index, so it differs per row and the parser
// must not depend on it.

let private detailPanel =
    """<span id="grdDommere_dxdt3_dommerinfo1_3_lblNavn_3">Stig Morten Bentsen</span>
<tr id="grdDommere_dxdt3_dommerinfo1_3_trMobil_3"><td>
<span id="grdDommere_dxdt3_dommerinfo1_3_lblMobilLede_3">Mobil:</span></td>
<td><span id="grdDommere_dxdt3_dommerinfo1_3_lblMobil_3">92242009</span></td></tr>
<tr id="grdDommere_dxdt3_dommerinfo1_3_trEpost1_3"><td>
<span id="grdDommere_dxdt3_dommerinfo1_3_lblEpost1Lede_3">Epost:</span></td>
<td><a id="grdDommere_dxdt3_dommerinfo1_3_hypEpost1_3"
href="mailto:a@b.no,c@d.com">a@b.no,c@d.com</a></td></tr>"""

[<Fact>]
let ``parseContact reads mobile and both addresses of a shared epost cell`` () =
    let c = parseContact detailPanel

    Assert.Equal<string list>([ "a@b.no"; "c@d.com" ], c.Emails)
    Assert.Equal<string list>([ "92242009" ], c.Phones)

[<Fact>]
let ``parseContact copes with an official who has only an address`` () =
    let panel =
        """<a id="grdDommere_dxdt0_dommerinfo1_0_hypEpost1_0"
           href="mailto:only@example.no">only@example.no</a>"""

    let c = parseContact panel

    Assert.Equal<string list>([ "only@example.no" ], c.Emails)
    Assert.Empty(c.Phones)

[<Fact>]
let ``parseContact returns nothing rather than failing on an empty panel`` () =
    let c = parseContact "<div>no contact rows at all</div>"

    Assert.Empty(c.Emails)
    Assert.Empty(c.Phones)

[<Fact>]
let ``parseContact reads a fixed line number too`` () =
    let panel =
        """<span id="grdDommere_dxdt1_dommerinfo1_1_lblTelefonLede_1">Telefon:</span>
           <span id="grdDommere_dxdt1_dommerinfo1_1_lblTelefon_1">66801234</span>"""

    Assert.Equal<string list>([ "66801234" ], (parseContact panel).Phones)

// ---- failure surfaces loudly ----

[<Fact>]
let ``a callback error in the payload is detected rather than read as an empty grid`` () =
    let payload = "/*DX*/({'generalError':'Callback request failed due to an internal server error.'})"

    Assert.True(isCallbackError payload)
    Assert.False(isCallbackError "/*DX*/({'result':{'html':'<table></table>'}})")
