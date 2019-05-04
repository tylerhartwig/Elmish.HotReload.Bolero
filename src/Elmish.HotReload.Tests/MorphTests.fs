module Elmish.HotReload.Tests.MorphTests

open Elmish.HotReload
open Xunit



type SameShapeRecordA = { name : string; age : int }

type SameShapeRecordB = { name : string; age : int }

[<Fact>]
let ``Can map records of same shape`` () =
    let a = { SameShapeRecordA.name = "Tyler"; age = 25 }

    let b = Morph.morph<SameShapeRecordB> a

    Assert.Equal (a.name, b.name)
    Assert.Equal (a.age, b.age)



type SameShapeUnionA =
    | Simple
    | OneValue of int

type SameShapeUnionB =
    | Simple
    | OneValue of int

[<Fact>]
let ``Can map unions of same shape`` () =
    let a1 = SameShapeUnionA.Simple
    let a2 = SameShapeUnionA.OneValue 42

    let b1 = Morph.morph<SameShapeUnionB> a1
    let b2 = Morph.morph<SameShapeUnionB> a2

    Assert.Equal(SameShapeUnionB.Simple, b1)
    Assert.Equal(SameShapeUnionB.OneValue 42, b2)



type UnionForRecordA = | Simple
type UnionForRecordB = | Simple

type RecordWithUnionA = { name : string; union : UnionForRecordA }
type RecordWithUnionB = { name : string; union : UnionForRecordB }

[<Fact>]
let ``Can map records with unions`` () =
    let a = { RecordWithUnionA.name = "Tyler"; union = UnionForRecordA.Simple }

    let b = Morph.morph<RecordWithUnionB> a

    Assert.Equal(a.name, b.name)
    Assert.Equal(UnionForRecordB.Simple, b.union)



type RecordForUnionA = { name: string; age: int }
type RecordForUnionB = { name: string; age: int }

type UnionWithRecordA = | Record of RecordForUnionA
type UnionWithRecordB = | Record of RecordForUnionB

[<Fact>]
let ``Can map union with record`` () =
    let aRecord = { RecordForUnionA.name = "Tyler"; age = 25 }
    let a = UnionWithRecordA.Record aRecord

    let (Record bRecord) = Morph.morph<UnionWithRecordB> a

    Assert.Equal (aRecord.name, bRecord.name)
    Assert.Equal (aRecord.age, bRecord.age)



type RecordRemoveFieldA = { name: string; age: int }
type RecordRemoveFieldB = { name: string }

[<Fact>]
let ``Can map record when removing field`` () =
    let a = { name = "Tyler"; age = 25 }

    let b = Morph.morph<RecordRemoveFieldB> a

    Assert.Equal(a.name, b.name)



type RecordAddFieldA = { name: string }
type RecordAddFieldB = { name: string; age: int }

[<Fact>]
let ``Can map record when adding field`` () =
    let a = { name = "Tyler" }

    let b = Morph.morph<RecordAddFieldB> a

    Assert.Equal(a.name, b.name)
    Assert.Equal(Unchecked.defaultof<int>, b.age)
