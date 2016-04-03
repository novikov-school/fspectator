namespace FSpectator
open System

type SubjectWrapper<'T> =
    {
        ParentSubject : SubjectWrapper<'T> option
        Initializer : 'T -> obj
        mutable Instance : obj
    }
    with
        static member create (f:'T->'a) parent = {
            Initializer = (fun ctx -> (f ctx) :> obj)
            ParentSubject = parent
            Instance = null }
        member self.Get ctx =
            if self.Instance = null then self.Instance <- self.Initializer ctx
            self.Instance

type TestContextData<'T> =
    { 
        MetaData: TestDataMap.T
        Disposables: System.IDisposable list
        WrappedSubject: SubjectWrapper<'T> option
        Data: TestDataMap.T }

type TestContextImpl(metaData:TestDataMap.T) =
    inherit TestContext()

    let data = ref {
        MetaData = metaData
        Data = metaData
        Disposables = []
        WrappedSubject = None }

    member ctx.RegisterDisposable x = 
        match (x :> obj) with
        | :? System.IDisposable as d -> 
            data := { (!data) with Disposables = d::(!data).Disposables }
            x
        | _ -> x

    member ctx.WithSubject s f =
        let tmp = (!data).WrappedSubject
        try
            data := { (!data) with WrappedSubject = s }
            f ctx
        finally
            data := { (!data) with WrappedSubject = tmp }

    override __.TryGet<'T> name : 'T option = 
        (!data).Data |> TestDataMap.tryGet<'T> name

    override __.Get<'T> name : 'T = (!data).Data.Get<'T> name

    override ctx.GetOrDefault<'T> name initializer:'T =
        match ctx.TryGet<'T> name with
        | Some x -> x
        | None -> 
            let result = initializer ctx
            ctx.Set name result
            result

    override ctx.Set<'T> name (value:'T) = 
        data := { (!data) with Data = (!data).Data.Add name value }
        ctx.RegisterDisposable value |> ignore

    override __.SetSubject<'T> (f:TestContext->'T) = 
        let wrapper = SubjectWrapper.create f (!data).WrappedSubject
        data := { (!data) with WrappedSubject = Some (wrapper) }
        
    override ctx.GetSubject<'T> ():'T = ctx.Subject :?> 'T
   
    override __.MetaData = metaData
    override ctx.Subject 
       with get () : obj =
            match (!data).WrappedSubject with
            | None -> null
            | Some subject -> 
                ctx.WithSubject     
                    subject.ParentSubject 
                    (subject.Get >> ctx.RegisterDisposable)

    interface IDisposable with
        member __.Dispose () = 
            (!data).Disposables
            |> List.iter (fun x -> x.Dispose())

    static member create (data:TestDataMap.T) =
        new TestContextImpl(data)
