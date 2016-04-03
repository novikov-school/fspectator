namespace FSpectator
open System
open Example

/// Represenations of the colors used to print to the console
type Color =
    | Red | Yellow | Green | DefaultColor
            
module Helper =
    let rec diffRev x y =
        match x, y with
        | x::xs, y::ys when x = y -> diffRev xs ys
        | _ -> (x,y)

    let diff x y = 
        let (x,y) = diffRev (x |> List.rev) (y |> List.rev)
        (x |> List.rev, y |> List.rev)

    let consolePrinter color (msg:string) =
        let old = System.Console.ForegroundColor 
        let consoleColor = 
            match color with
            | Red -> System.Console.ForegroundColor <- ConsoleColor.Red
            | Yellow -> System.Console.ForegroundColor <- ConsoleColor.Yellow
            | Green -> System.Console.ForegroundColor <- ConsoleColor.Green
            | DefaultColor -> ()
        try 
            System.Console.Write msg
        finally
            System.Console.ForegroundColor <- old

type IReporter = 
    abstract member BeginGroup : ExampleDescriptor -> IReporter
    abstract member ReportExample : ExampleDescriptor -> TestResultType -> IReporter
    abstract member EndTestRun : unit -> obj
    abstract member EndGroup : unit -> IReporter

type Reporter<'T> = {
    BeginGroup : ExampleDescriptor -> 'T -> 'T
    ReportExample: ExampleDescriptor -> TestResultType -> 'T -> 'T
    EndTestRun: 'T -> 'T
    EndGroup: 'T -> 'T
    Success: 'T -> bool
    BeginTestRun: unit -> 'T }

module TreeReporterOptions =
    type T = {
        Printer: Color->string->unit
        PrintSuccess: bool
    }
    let Default = {
        Printer = Helper.consolePrinter
        PrintSuccess = true
    }

module TreeReporter =
    type ExecutedExample = {
        Example: ExampleDescriptor
        Result : TestResultType
        ContainingGroups: ExampleDescriptor List }

    type T = {
        ExecutedExamples: ExecutedExample list
        Groups: ExampleDescriptor list }
    let Zero = { 
        Groups = []
        ExecutedExamples = [] }

    let result ex = ex.Result
    let beginGroup exampleGroup report =
        { report with Groups = exampleGroup :: report.Groups }

    let endGroup report = { report with Groups = report.Groups.Tail }

    let reportExample example result report =
        let executedExample = 
            { Example = example; ContainingGroups = report.Groups; Result = result }
        { report with ExecutedExamples = executedExample :: report.ExecutedExamples }

    let getSummary report =
        let folder (success,pending,fail) = function
            | Failure _ | Error _ -> (success,pending,fail+1)
            | Pending -> (success,pending+1,fail)
            | Success -> (success+1,pending,fail)
        report.ExecutedExamples |> 
        List.map result |> 
        List.fold folder (0,0,0)

    let printSummary (options:TreeReporterOptions.T) report =
        let printer = options.Printer
        let exampleName x = x.Example.Name
        let printFailedExamples executedExamples =
            let rec print indent prevGroups executedExamples = 
                match executedExamples with
                | [] -> ()
                | x::xs ->
                    let (pop,push) = Helper.diff prevGroups (x.ContainingGroups |> List.map (fun x -> x.Name))
                    let indent = pop |> List.fold (fun (i:string list) y -> i.Tail) indent
                    let indentation = indent |> List.fold (+) ""
                    let prevGroups = pop |> List.fold (fun (i:string list) y -> i.Tail) prevGroups
                    match push |> List.rev with
                    | y::ys ->  
                        sprintf "%s%s\n" indentation y |> printer DefaultColor
                        print ("  "::indent) (y::prevGroups) executedExamples
                    | _ ->
                        sprintf "%s- %s - " indentation (x |> exampleName) |> printer DefaultColor
                        match result x with
                        | Failure _ | Error _ -> 
                            "FAILED\n" |> printer Red
                            sprintf "%A\n" x.Result |> printer DefaultColor
                        | Pending -> "PENDING\n" |> printer Yellow
                        | Success -> "SUCCESS\n" |> printer Green
                        print indent prevGroups xs     

            let filter =
                match options.PrintSuccess with
                | true -> fun _ -> true
                | _ -> 
                    fun ex -> 
                        match result ex with
                        | Success -> false
                        | _ -> true

            let failed executedExample = 
                match result executedExample with
                | Success -> false
                | _ -> true

            executedExamples 
            |> List.filter filter
            |> List.rev
            |> (print [] [])
        let (success,pending,failed) =  getSummary report
        match (failed,pending) with
        | (0,0) -> ()
        | (0,_) -> "There are pending examples: \n" |> printer Yellow
        | _ -> "There are failed examples: \n" |> printer Red
        report.ExecutedExamples |> printFailedExamples
        sprintf "%d success, %d pending, %d failed\n" success pending failed |> printer DefaultColor
        report

    type Reporter(options:TreeReporterOptions.T) as self =
        let mutable state = Zero
        let reporter = self :> IReporter
        let update f = 
            state <- f state
            reporter
        let success report = 
            let (_,_,failed) =  getSummary report
            failed = 0

        interface IReporter with
            member __.BeginGroup x = update (beginGroup x)
            member __.EndGroup () = update endGroup
            member __.ReportExample x r = update (reportExample x r)
            member __.EndTestRun () = printSummary options state :> obj
