using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Techies
{
    [EditorCommandClass]
    public class OpenUpmCmd
    {
        // openupm add com.my.package@1.2.3
        [EditorCommand("openupm", "add", Description = "Add package com.my.package@1.2.3", Usage = "openupm add <package>@<version>")]
        public string Add(string package)
        {
            string projectPath = Directory.GetCurrentDirectory();
            string manifestPath = Path.Combine(projectPath, "Packages/manifest.json");

            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            JObject dependencies = manifest["dependencies"] as JObject ?? new JObject();
            JArray scopedRegistries = manifest["scopedRegistries"] as JArray ?? new JArray();

            var packageParts = package.Split('@');
            string packageName = packageParts[0];
            string packageVersion = packageParts.Length > 1 ? packageParts[1] : null;

            if (dependencies.ContainsKey(packageName))
            {
                return $"OpenUPM {package} is already in the manifest.";
            }

            var existing = scopedRegistries.FirstOrDefault(r => r["name"]?.ToString().ToLower() == "openupm") as JObject;

            var openupm = existing ?? new JObject
            {
                ["name"] = "openupm",
                ["url"] = "https://package.openupm.com",
                ["scopes"] = new JArray(packageName)
            };

            if (existing == null)
            {
                scopedRegistries.Add(openupm);
            }
            else
            {
                var scopes = openupm["scopes"].ToObject<List<string>>();
                if (!scopes.Contains(packageName))
                {
                    scopes.Add(packageName);
                    openupm["scopes"] = JArray.FromObject(scopes);
                }
            }

            manifest["scopedRegistries"] = scopedRegistries;
            File.WriteAllText(manifestPath, manifest.ToString());

            Client.Add(packageName);
            
            return $"OpenUPM {packageName} added with version {packageVersion ?? "latest"}";
        }

        [EditorCommand("openupm", "remove", Description = "Remove package com.my.package", Usage = "openupm add <package>")]
        public string Remove(string package)
        {
            string projectPath = Directory.GetCurrentDirectory();
            string manifestPath = Path.Combine(projectPath, "Packages/manifest.json");

            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            JObject dependencies = manifest["dependencies"] as JObject ?? new JObject();
            JArray scopedRegistries = manifest["scopedRegistries"] as JArray ?? new JArray();

            var packageParts = package.Split('@');
            string packageName = packageParts[0];
            string packageVersion = packageParts.Length > 1 ? packageParts[1] : null;

            var isChanged = dependencies.Remove(packageName);

            var openupm = scopedRegistries.FirstOrDefault(r => r["name"]?.ToString().ToLower() == "openupm") as JObject;
            if (openupm != null)
            {
                var scopes = openupm["scopes"].ToObject<List<string>>();
                if (scopes != null)
                {
                    var removed = scopes.Remove(packageName);
                    isChanged = isChanged || removed;
                    openupm["scopes"] = JArray.FromObject(scopes);
                }
            }

            if (!isChanged)
            {
                return $"OpenUPM {package} not found in the manifest.";
            }

            manifest["scopedRegistries"] = scopedRegistries;
            manifest["dependencies"] = dependencies;
            File.WriteAllText(manifestPath, manifest.ToString());

            Client.Resolve();
            return $"OpenUPM {package} removed";
        }
    }
}