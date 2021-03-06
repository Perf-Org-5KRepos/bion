// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;

using BSOA.IO;
using BSOA.Json;
using BSOA.Test.Components;

using Xunit;

namespace BSOA.Test.Model
{
    public class DatabaseTests
    {
        [Fact]
        public void Database_Basics()
        {
            V1.Community community = new V1.Community();
            community.People.Add(new V1.Person() { Age = 39, Name = "Scott" });
            community.People.Add(new V1.Person() { Age = 36, Name = "Adam" });

            // Use ReadOnlyList.VerifySame to check count, enumerators, and indexer
            community.DB.Save("V1.Community.bsoa", TreeFormat.Binary);
            V1.Community roundTripped = new V1.Community();
            roundTripped.DB.Load("V1.Community.bsoa", TreeFormat.Binary);
            CollectionReadVerifier.VerifySame(community.People, roundTripped.People);

            // Try loading database with size diagnostics
            TreeDiagnostics diagnostics = TreeSerializer.Diagnostics(community.DB, () => new V1.Community().DB, TreeFormat.Binary);

            // Verify table and column names in diagnostics
            string text = diagnostics.ToString();
            Assert.Contains("Person", text);
            Assert.Contains("Age", text);
            Assert.Contains("Name", text);

            // Verify Person has two columns, Write doesn't throw
            Assert.Equal("Person", diagnostics.Children[0].Name);
            Assert.Equal(Names.Columns, diagnostics.Children[0].Children[0].Name);
            Assert.Equal(2, diagnostics.Children[0].Children[0].Children.Count);
            diagnostics.Write(Console.Out, 3);

            // Verify Trim doesn't throw (results not visible)
            community.DB.Trim();
            CollectionReadVerifier.VerifySame(community.People, roundTripped.People);

            // Verify Database.Clear works
            community.DB.Clear();
            Assert.Empty(community.People);
        }

        [Fact]
        public void Database_ReplaceColumn()
        {
            // Test saving a database and then loading it into a different object model with added and removed columns.
            
            V1.Community v1 = new V1.Community();
            v1.People.Add(new V1.Person() { Age = 39, Name = "Scott" });
            v1.People.Add(new V1.Person() { Age = 36, Name = "Adam" });

            string filePath = "People.bsoa.bin";

            // Save V1 (Age and Name)
            v1.DB.Save(filePath, TreeFormat.Binary);

            // Load as V2 (BirthDate and Name)
            V2.Community v2 = new V2.Community();
            v2.DB.Load(filePath, TreeFormat.Binary);

            // Verify row count the same, Name loaded properly
            Assert.Equal(v1.People.Count, v2.People.Count);
            Assert.Equal(v1.People[0].Name, v2.People[0].Name);

            DateTime birthdate = DateTime.Parse("1981-01-01").ToUniversalTime();
            v2.People[0].Birthdate = birthdate;

            // Verify new database serializes new column
            V2.Community v2RoundTrip = new V2.Community();

            v2.DB.Save(filePath, TreeFormat.Binary);
            v2RoundTrip.DB.Load(filePath, TreeFormat.Binary);

            Assert.Equal(birthdate, v2RoundTrip.People[0].Birthdate);
            CollectionReadVerifier.VerifySame(v2.People, v2RoundTrip.People);

            // Load *new format* into V1 object model
            V1.Community v1RoundTrip = new V1.Community();
            v1RoundTrip.DB.Load(filePath, TreeFormat.Binary);

            // Verify unchanged columns come back
            Assert.Equal(v1.People.Count, v1RoundTrip.People.Count);
            Assert.Equal(v1.People[0].Name, v1RoundTrip.People[0].Name);

            // Verify Age empty
            Assert.Equal(0, v1RoundTrip.People[0].Age);

            // Read with TreeSerializationSettings.Strict and verify error
            Assert.Throws<IOException>(() => v1RoundTrip.DB.Load(filePath, TreeFormat.Binary, new BSOA.IO.TreeSerializationSettings() { Strict = true }));
        }

        [Fact]
        public void Database_NewtonsoftCompatibility()
        {
            // Test that basic Database, Table, and Row types can successfully roundtrip via Newtonsoft.Json serialization by default.
            // These use generated JsonConverter classes to serialize using safe constructors and good default behaviors.

            V1.Community v1 = new V1.Community();
            v1.People.Add(new V1.Person() { Age = 39, Name = "Scott" });
            v1.People.Add(new V1.Person() { Age = 36, Name = "Adam" });

            string serializeToPath = Path.GetFullPath("Community.V1.json");
            AsJson.Save(serializeToPath, v1, verbose: true);

            V1.Community roundTrip = AsJson.Load<V1.Community>(serializeToPath);
            CollectionReadVerifier.VerifySame(v1.People, roundTrip.People);

            AsJson.Save(serializeToPath, v1, verbose: true);
            roundTrip = AsJson.Load<V1.Community>(serializeToPath);
            CollectionReadVerifier.VerifySame(v1.People, roundTrip.People);
        }
    }
}
