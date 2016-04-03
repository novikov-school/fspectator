﻿module FSpectator.SelfTests.TestContextSpecs
open FSpectator
open Dsl
open Matchers

type DisposeSpy () =
    member val Disposed = false with get, set
    interface System.IDisposable with
        member self.Dispose () = self.Disposed <- true
let create = TestContextImpl.create
let createContext () = TestDataMap.Zero |> create
let dispose (ctx:TestContextImpl) =
    (ctx :> System.IDisposable).Dispose()

let haveContextData name valueMatcher =
    createCompoundMatcher 
        valueMatcher
        (fun (x:TestContext) -> x.Get name)
        (sprintf "have data key %A with value %s" name (valueMatcher.ExpectationMsgForShould))
        
let specs =
    describe "TestContext" [
        describe "context data" [
            it "is initialized from metadata" <| fun _ ->
                let metaData = TestDataMap.create [("answer", 42)]
                let context = metaData |> create
                context.Should (haveContextData "answer" (equal 42))

            it "does not change original metadata" <| fun _ ->
                let metaData = TestDataMap.create [("answer", 42)]
                let context = metaData |> create
                context?answer <- 43
                metaData?answer.Should (equal 42)
        ]

        describe "set and get data" [
            let itCanLookupTheData =
                examples [
                    it "can be retrieved using 'get'" 
                        (fun ctx -> ctx.Get "answer" |> should (be.equalTo 42))
                    
                    it "can be retrieved using dynamic operator" 
                        (fun ctx -> ctx?answer |> should (be.equalTo 42))
                ]
                
            yield context "data initialized with dynamic operator" [
                before (fun ctx -> ctx?answer <- 42)
                itCanLookupTheData
            ]

            yield context "data initialized with 'set' function" [
                before (fun ctx -> ctx.Set "answer" 42)
                itCanLookupTheData
            ]
        ]

        describe "Get" [
            context "when data not initialized" [
                it "throws descriptive message" <| fun ctx ->
                    let test () = ctx.Get "dummy"
                    test |> should (throwException.withMessageContaining "\"dummy\" not found")
            ]

            context "when data is of unexpected type" [
                it "throws a descriptive message" <| fun ctx ->
                    ctx?answer <- "42"
                    let test () = ctx.Get<int> "answer" |> ignore
                    test |> should (throwException.withMessage 
                        (be.string.matching "Expected.*Int.*was.*String"))
            ]
        ]

        describe "getOrDefault" [
            before (fun c ->
                c?call_count <- 0
                c?initializer <- fun (_:TestContext) -> 
                    c?call_count <- c?call_count + 1
                    "initialized value")
                
            context "when no data has been added" [
                it "initializes the default value" <| fun c ->
                    c.GetOrDefault "value" c?initializer
                    |> should (equal "initialized value")

                it "returns the same value the 2nd time" <| fun c ->
                    c.GetOrDefault<string> "value" c?initializer |> ignore
                    c.GetOrDefault<string> "value" c?initializer |> ignore
                    c?call_count.Should (equal 1)
            ]

            context "when data has already been added" [
                it "returns the added data" <| fun c ->
                    c?value <- "manually set"
                    c.GetOrDefault "value" c?initializer
                    |> should (equal "manually set")
            ]
        ]

        describe "tryGet" [
            context "data initialized in the context" [
                before (fun c -> c?data <- 42)
                
                it "retrieves the expected data" (fun c ->
                    match c.TryGet "data" with
                    | Some x -> x |> should (be.equalTo 42)
                    | None -> failwith "Data not found"
                )
            ]

            context "data not initialized in the context" [
                it "returns none" (fun c ->
                    match c.TryGet "data" with
                    | None -> ()
                    | _ -> failwith "Data should not be found"
                )
            ]
        ]

        describe "subject" [
            before <| fun ctx ->
                ctx?callCount <- 0
                ctx.SetSubject <| fun _ ->
                    ctx?callCount <- 1 + ctx?callCount

            context "when subject is not requested" [
                it "does not evaluate subject initialization code" <| fun ctx ->
                    ctx?callCount.Should (equal 0)
            ]

            context "when subject is requested twice" [
                it "only call initialization code once" <| fun ctx ->
                    ctx.Subject |> ignore
                    ctx.Subject |> ignore
                    ctx?callCount.Should (equal 2)
            ]
        ]

        describe "cleanup" [
            it "calls dispose on objects" <| fun _ ->
                let x = new DisposeSpy()
                let ctx = createContext ()
                ctx?x <- x
                ctx |> dispose
                x.Disposed |> should be.True

            it "disposes instances, that are no longer present" <| fun _ ->
                let x = new DisposeSpy()
                let y = new DisposeSpy()
                let ctx = createContext ()
                ctx?x <- x
                ctx?x <- y
                ctx |> dispose
                x.Disposed |> should be.True

            it "calls dispose on Subject" (fun _ ->
                let x = new DisposeSpy()
                let ctx = createContext ()
                ctx.SetSubject (fun _ -> x)
                ctx.Subject |> ignore // make sure it is evaluated
                ctx |> dispose
                x.Disposed |> should be.True
            )
        ]
    ]
