module DownloadUtils

open System
open System.Globalization
open System.Net
open System.Net.Http
open System.IO

type YearMonth = {year : int; month : string}
    

let parseFilename (filename:string) =
    //example : 20251025KolbotnIL_SkiSKRoaldAmundsenCUP.xml
    let year = filename[0..3] |> int32
    let month = filename[4..5] |> int32
   
    let c = CultureInfo("nb-no")
        
    { YearMonth.year = year; month = c.DateTimeFormat.GetMonthName month }  

let getStevneoppsettFile  (targetDir:string) (filename:string) =
    // https://www.medley.no/dokumenter/stevneoppsett/2025/Oktober/20251025KolbotnIL_SkiSKRoaldAmundsenCUP.xml
    let targetFilename = Path.Combine [|targetDir; filename|]
    if not (File.Exists(targetFilename)) then
        task {
            use file = File.OpenWrite(targetFilename)

            use client = new HttpClient()
            client.BaseAddress <- Uri("https://www.medley.no/dokumenter/stevneoppsett/")
            
            let date = parseFilename filename
            
            let! response = client.GetStreamAsync($"{date.year}/{date.month}/{filename}")
            do! response.CopyToAsync(file)
        }
        |> Async.AwaitTask
        |> Async.RunSynchronously
        printfn $"File %s{filename} created"
    else
        printfn $"File %s{filename} already exists"    
