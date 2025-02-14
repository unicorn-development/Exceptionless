﻿using Exceptionless.Core.Extensions;
using Exceptionless.Core.Models.Data;
using Exceptionless.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Exceptionless.Core.Plugins.EventUpgrader;

[Priority(2000)]
public class V2EventUpgrade : PluginBase, IEventUpgraderPlugin
{
    public V2EventUpgrade(AppOptions options, ILoggerFactory loggerFactory) : base(options, loggerFactory) { }

    public void Upgrade(EventUpgraderContext ctx)
    {
        if (ctx.Version > new Version(2, 0))
            return;

        foreach (var doc in ctx.Documents.OfType<JObject>())
        {
            bool isNotFound = doc.GetPropertyStringValue("Code") == "404";

            if (ctx.IsMigration)
            {
                doc.Rename("ErrorStackId", "StackId");
            }
            else
            {
                doc.RenameOrRemoveIfNullOrEmpty("Id", "ReferenceId");
                doc.Remove("OrganizationId");
                doc.Remove("ProjectId");
                doc.Remove("ErrorStackId");
            }

            doc.RenameOrRemoveIfNullOrEmpty("OccurrenceDate", "Date");
            doc.Remove("ExceptionlessClientInfo");
            if (!doc.RemoveIfNullOrEmpty("Tags"))
            {
                var tags = doc.GetValue("Tags");
                if (tags is not null && tags.Type == JTokenType.Array)
                {
                    foreach (var tag in tags.ToList())
                    {
                        string t = tag.ToString();
                        if (String.IsNullOrEmpty(t) || t.Length > 255)
                            tag.Remove();
                    }
                }
            }

            doc.RenameOrRemoveIfNullOrEmpty("RequestInfo", "@request");
            bool hasRequestInfo = doc["@request"] is not null;

            if (!isNotFound)
                doc.RenameOrRemoveIfNullOrEmpty("EnvironmentInfo", "@environment");
            else
                doc.Remove("EnvironmentInfo");

            doc.RenameAll("ExtendedData", "Data");

            var extendedData = doc.Property("Data") is not null ? doc.Property("Data")!.Value as JObject : null;
            if (extendedData is not null)
            {
                if (!isNotFound)
                    extendedData.RenameOrRemoveIfNullOrEmpty("TraceLog", "@trace");
                else
                    extendedData.Remove("TraceLog");
            }

            if (isNotFound && hasRequestInfo)
            {
                doc.RemoveAll("Code", "Type", "Message", "Inner", "StackTrace", "TargetMethod", "Modules");
                if (extendedData?["__ExceptionInfo"] is not null)
                    extendedData.Remove("__ExceptionInfo");

                doc.Add("Type", new JValue("404"));
            }
            else
            {
                var error = new JObject();

                if (!doc.RemoveIfNullOrEmpty("Message"))
                    error.Add("Message", doc["Message"]!.Value<string>());

                error.MoveOrRemoveIfNullOrEmpty(doc, "Code", "Type", "Inner", "StackTrace", "TargetMethod", "Modules");

                // Copy the exception info from root extended data to the current errors extended data.
                if (extendedData?["__ExceptionInfo"] is not null)
                {
                    error.Add("Data", new JObject());
                    ((JObject)error["Data"]!).MoveOrRemoveIfNullOrEmpty(extendedData, "__ExceptionInfo");
                }

                string? id = doc["Id"]?.Value<string>();
                RenameAndValidateExtraExceptionProperties(id, error);

                var inner = error["Inner"] as JObject;
                while (inner is not null)
                {
                    RenameAndValidateExtraExceptionProperties(id, inner);
                    inner = inner["Inner"] as JObject;
                }

                doc.Add("Type", new JValue(isNotFound ? "404" : "error"));
                doc.Add("@error", error);
            }

            string? emailAddress = doc.GetPropertyStringValueAndRemove("UserEmail");
            string? userDescription = doc.GetPropertyStringValueAndRemove("UserDescription");
            if (!String.IsNullOrWhiteSpace(emailAddress) && !String.IsNullOrWhiteSpace(userDescription))
                doc.Add("@user_description", JObject.FromObject(new UserDescription(emailAddress, userDescription)));

            string? identity = doc.GetPropertyStringValueAndRemove("UserName");
            if (!String.IsNullOrWhiteSpace(identity))
                doc.Add("@user", JObject.FromObject(new UserInfo(identity)));

            doc.RemoveAllIfNullOrEmpty("Data", "GenericArguments", "Parameters");
        }
    }

    private void RenameAndValidateExtraExceptionProperties(string? id, JObject error)
    {
        var extendedData = error?["Data"] as JObject;
        if (extendedData?["__ExceptionInfo"] is null)
            return;

        string json = extendedData["__ExceptionInfo"]!.ToString();
        extendedData.Remove("__ExceptionInfo");

        if (String.IsNullOrWhiteSpace(json))
            return;

        if (json.Length > 200000)
        {
            _logger.LogError("__ExceptionInfo on {id} is Too Big: {Length}", id, json.Length);
            return;
        }

        var ext = new JObject();
        try
        {
            var extraProperties = JObject.Parse(json);
            foreach (var property in extraProperties.Properties())
            {
                if (property.IsNullOrEmpty())
                    continue;

                string dataKey = property.Name;
                if (extendedData[dataKey] is not null)
                    dataKey = "_" + dataKey;

                ext.Add(dataKey, property.Value);
            }
        }
        catch (Exception) { }

        if (ext.IsNullOrEmpty())
            return;

        extendedData.Add("@ext", ext);
    }
}
