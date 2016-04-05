﻿//MIT License
//
//Copyright (c) 2016 Robert Peele
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

namespace GraphQL.Parser.Test
open GraphQL.Parser
open GraphQL.Parser.SchemaAST
open Microsoft.VisualStudio.TestTools.UnitTesting

// Tests that the schema resolution code works as expected with a pretend schema.

// Metadata type of our fake schema is just a string.
type FakeData = string

type NameArgument() =
    interface ISchemaArgument<FakeData> with
        member this.ArgumentName = "name"
        member this.ArgumentType = PrimitiveType StringType
        member this.Description = Some "argument for filtering by name"
        member this.Info = "Fake name arg info"

type IdArgument() =
    interface ISchemaArgument<FakeData> with
        member this.ArgumentName = "id"
        member this.ArgumentType = PrimitiveType IntType
        member this.Description = Some "argument for filtering by id"
        member this.Info = "Fake id arg info"

type UserType() =
    member private this.Field(name, fieldType : SchemaFieldType<FakeData>, args) =
        { new ISchemaField<FakeData> with
            member __.DeclaringType = upcast this
            member __.FieldType = fieldType
            member __.FieldName = name
            member __.Description = Some ("Description of " + name)
            member __.Info = "Info for " + name
            member __.Arguments = args |> dictionary :> _
        }
    interface ISchemaQueryType<FakeData> with
        member this.TypeName = "User"
        member this.Description = Some "Complex user type"
        member this.Info = "Fake user type info"
        member this.Fields =
            [|
                "id", this.Field("id", ValueField { Nullable = false; Type = PrimitiveType IntType }, [||])
                "name", this.Field("name", ValueField { Nullable = false; Type = PrimitiveType StringType }, [||])
                "friend", this.Field("friend", QueryField (this :> ISchemaQueryType<_>),
                    [|
                        "name", new NameArgument() :> _
                        "id", new IdArgument() :> _
                    |])
            |] |> dictionary :> _

type RootType() =
    member private this.Field(name, fieldType : SchemaFieldType<FakeData>, args) =
        { new ISchemaField<FakeData> with
            member __.DeclaringType = upcast this
            member __.FieldType = fieldType
            member __.FieldName = name
            member __.Description = Some ("Description of " + name)
            member __.Info = "Info for " + name
            member __.Arguments = args |> dictionary :> _
        }
    interface ISchemaQueryType<FakeData> with
        member this.TypeName = "Root"
        member this.Description = Some "Root context type"
        member this.Info = "Fake root type info"
        member this.Fields =
            [|
                "user", this.Field("user", QueryField (new UserType()),
                    [|
                        "name", new NameArgument() :> _
                        "id", new IdArgument() :> _
                    |])
            |] |> dictionary :> _

type FakeSchema() =
    let root = new RootType() :> ISchemaQueryType<_>
    let types =
        [
            root
            new UserType() :> _
        ]
    interface ISchema<FakeData> with
        member this.ResolveDirectiveByName(name) = None // no directives
        member this.ResolveEnumValueByName(name) = None // no enums
        member this.ResolveVariableTypeByName(name) = None // no named types
        member this.ResolveQueryTypeByName(name) =
            types |> List.tryFind (fun ty -> ty.TypeName = name)
        member this.RootType = root
        

[<TestClass>]
type SchemaTest() =
    let schema = new FakeSchema() :> ISchema<_>
    let good source =
        let doc = GraphQLDocument.Parse(schema, source)
        if doc.Operations.Count <= 0 then
            failwith "No operations in document!"
    let bad reason source =
        try
            ignore <| GraphQLDocument.Parse(schema, source)
            failwith "Document resolved against schema when it shouldn't have!"
        with
        | :? ValidationException as ex ->
            if (ex.Message.Contains(reason)) then ()
            else reraise()
    [<TestMethod>]
    member __.TestGoodUserQuery() =
        good @"
{
    user(id: 1) {
        id
        name
        friend(name: ""bob"") {
            id
            name
        }
    }
}
"

    [<TestMethod>]
    member __.TestBogusArgument() =
        bad "unknown argument ``occupation''" @"
{
    user(id: 1) {
        id
        name
        friend(occupation: ""welder"") {
            id
            name
        }
    }
}
"

    [<TestMethod>]
    member __.TestBogusRootField() =
        bad "``team'' is not a field of type ``Root''" @"
{
    team {
        id
        name
    }
}
"

    [<TestMethod>]
    member __.TestBogusSubField() =
        bad "``parent'' is not a field of type ``User''" @"
{
    user {
        id
        name
        parent {
            id
            name
        }
    }
}
"

    [<TestMethod>]
    member __.TestRecursionDepth() =
        bad "exceeded maximum recursion depth" @"
fragment friendNamedBobForever on User {
    friend(name: ""bob"") {
        ...friendNamedBobForever
    }
}
{
    user {
        ...friendNamedBobForever
    }
}
"