﻿using BSOA.Generator.Schema;
using BSOA.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BSOA.Generator
{
    public class FullGenerator
    {
        private string SchemaPath { get; }
        private string OutputFolder { get; }
        private string TemplateDefaultPath { get; }
        private string TemplateOverridesFolderPath { get; }
        private string PostReplacementsPath { get; }

        public FullGenerator(string schemaPath, string outputFolder, string templateOverridesFolderPath = null, string postReplacementPath = null)
        {
            SchemaPath = schemaPath;
            OutputFolder = outputFolder;
            TemplateDefaultPath = FindTemplateDefaultPath();
            TemplateOverridesFolderPath = templateOverridesFolderPath;
            PostReplacementsPath = postReplacementPath;
        }

        public void Generate()
        {
            Console.WriteLine($"Generating BSOA object model from schema\r\n  '{SchemaPath}' at \r\n  '{OutputFolder}'...");

            Database db = AsJson.Load<Database>(SchemaPath);

            if (Directory.Exists(OutputFolder)) { Directory.Delete(OutputFolder, true); }
            Directory.CreateDirectory(OutputFolder);

            Dictionary<string, string> postReplacements = new Dictionary<string, string>();
            if (PostReplacementsPath != null)
            {
                postReplacements = AsJson.Load<Dictionary<string, string>>(PostReplacementsPath);
            }

            // List and Dictionary read and write methods need a writeValue delegate passed
            postReplacements["me.([^ ]+) = JsonToIList<([^>]+)>.Read\\(reader, root\\)"] = "JsonToIList<$2>.Read(reader, root, me.$1, JsonTo$2.Read)";
            postReplacements["JsonToIList<([^>]+)>.Write\\(writer, ([^,]+), item.([^,]+), default\\);"] = "JsonToIList<$1>.Write(writer, $2, item.$3, JsonTo$1.Write);";

            postReplacements["me.([^ ]+) = JsonToIDictionary<String, ([^>]+)>.Read\\(reader, root\\)"] = @"me.$1 = JsonToIDictionary<String, $2>.Read(reader, root, null, JsonTo$2.Read)";
            postReplacements["JsonToIDictionary<String, ([^>]+)>.Write\\(writer, ([^,]+), item.([^,]+), default\\);"] = "JsonToIDictionary<String, $1>.Write(writer, $2, item.$3, JsonTo$1.Write);";

            // Generate Database class
            new ClassGenerator(TemplateType.Database, TemplatePath(@"Internal\CompanyDatabase.cs"), @"Internal\{0}.cs", postReplacements)
                .Generate(OutputFolder, db);

            // Generate Tables
            new ClassGenerator(TemplateType.Table, TemplatePath(@"Internal\TeamTable.cs"), @"Internal\{0}Table.cs", postReplacements)
                .Generate(OutputFolder, db);

            // Generate Entities
            new ClassGenerator(TemplateType.Table, TemplatePath(@"Team.cs"), "{0}.cs", postReplacements)
                .Generate(OutputFolder, db);

            // Generate Root Entity (overwrite normal entity form)
            new ClassGenerator(TemplateType.Table, TemplatePath(@"Company.cs"), @"{0}.cs", postReplacements)
                .Generate(OutputFolder, db.Tables.Where((table) => table.Name.Equals(db.RootTableName)).First(), db);

            // Generate Entity Json Converter
            new ClassGenerator(TemplateType.Table, TemplatePath(@"Json\JsonToTeam.cs"), @"Json\JsonTo{0}.cs", postReplacements)
                .Generate(OutputFolder, db);

            // Generate Root Entity Json Converter (overwrite normal entity form)
            new ClassGenerator(TemplateType.Table, TemplatePath(@"Json\JsonToCompany.cs"), @"Json\JsonTo{0}.cs", postReplacements)
                .Generate(OutputFolder, db.Tables.Where((table) => table.Name.Equals(db.RootTableName)).First(), db);

            Console.WriteLine("Done.");
            Console.WriteLine();
        }

        private string FindTemplateDefaultPath()
        {
            string templatePath;
            string exePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            templatePath = Path.Combine(exePath, "Templates");
            if (Directory.Exists(templatePath)) { return templatePath; }

            return Path.Combine(Environment.CurrentDirectory, "Templates");
        }

        private string TemplatePath(string relativeFilePath)
        {
            string candidatePath;

            if (!string.IsNullOrEmpty(TemplateOverridesFolderPath))
            {
                candidatePath = Path.Combine(TemplateOverridesFolderPath, relativeFilePath);
                if (File.Exists(candidatePath)) { return candidatePath; }
            }

            return Path.Combine(TemplateDefaultPath, relativeFilePath);
        }
    }
}
