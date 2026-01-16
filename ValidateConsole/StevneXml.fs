module StevneXml

open FSharp.Data 

type MeetSetup = XmlProvider<".\\Sample\\20251025KolbotnIL_SkiSKRoaldAmundsenCUP.xml", Encoding = "ISO-8859-1">