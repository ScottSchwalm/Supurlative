﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Http;

namespace RimDev.Supurlative
{
    public static class Extensions
    {
        public static IList<string> GetNames(this HttpRouteCollection routeCollection)
        {
            object routes = routeCollection;
            var fieldName = "_dictionary";

            if (routes == null) return new List<string>();

            var type = routes.GetType();

            if (type.FullName == "System.Web.Http.WebHost.Routing.HostedHttpRouteCollection")
            {
                var hostedField = type.GetField("_routeCollection", BindingFlags.NonPublic | BindingFlags.Instance);
                type = hostedField.FieldType;
                routes = hostedField.GetValue(routes);
                fieldName = "_namedMap";
            }

            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var dictionary = field.GetValue(routes) as IDictionary;

            return dictionary == null
                ? new List<string>() 
                : dictionary.Keys.Cast<string>().ToList();
        }

        public static bool CheckIfAnonymousType(this Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // HACK: The only way to detect anonymous types right now.
            return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false)
                && type.IsGenericType && type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                && (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }

        internal static IDictionary<string, object> TraverseForKeys(
            this object target,
            SupurlativeOptions options,
            string parentKey = null)
        {
            var kvp = new Dictionary<string, object>();

            if (target == null)
                return kvp;

            var valueType = target as Type == null
                ? target.GetType()
                : target as Type;

            var properties = valueType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.CanWrite || valueType.CheckIfAnonymousType());

            foreach (var property in properties)
            {
                var fullPropertyName = parentKey == null
                        ? property.Name
                        : string.Format("{0}{1}{2}", parentKey, options.PropertyNameSeperator, property.Name);

                object valueOrPropertyType = property.PropertyType;

                if (target as Type == null)
                {
                    valueOrPropertyType = property.GetValue(target, null)
                        ?? property.PropertyType;
                }

                var formatterAttribute = property.GetCustomAttributes()
                    .Where(x => typeof(BaseFormatterAttribute).IsAssignableFrom(x.GetType()))
                    .Cast<BaseFormatterAttribute>()
                    .FirstOrDefault();

                if (formatterAttribute == null)
                {
                    // find any global formatters
                    formatterAttribute =
                        options
                        .Formatters
                        .Where(x => x.IsMatch(property.PropertyType, options))
                        .FirstOrDefault();
                }

                if (formatterAttribute != null)
                {
                    var targetValue = target == null || target as Type != null
                        ? null
                        : property.GetValue(target, null);

                    try
                    {
                        formatterAttribute.Invoke(
                            fullPropertyName,
                            targetValue,
                            property.PropertyType,
                            kvp,
                            options);
                    }
                    catch (Exception ex)
                    {
                        throw new FormatterException(
                            string.Format("There is a problem invoking the formatter: {0}.", formatterAttribute.GetType().FullName),
                            ex);
                    }
                }
                else
                {
                    var kvpValue = (valueOrPropertyType != null && valueOrPropertyType as Type == null
                            ? valueOrPropertyType.ToString()
                            : null);

                    if (property.PropertyType.IsPrimitive
                        || (!string.IsNullOrEmpty(property.PropertyType.Namespace)
                        && property.PropertyType.Namespace.StartsWith("System")))
                    {
                        kvp.Add(fullPropertyName, kvpValue);
                    }
                    else
                    {
                        var results = TraverseForKeys(valueOrPropertyType, options, fullPropertyName);

                        if (results.Count() == 0)
                        {
                            kvp.Add(fullPropertyName, kvpValue);
                        }
                        else
                        {
                            foreach (var result in results)
                            {
                                kvp.Add(result.Key, result.Value);
                            }
                        }
                    }
                }
            }

            return kvp.ToDictionary(
                x => options.LowercaseKeys ? x.Key.ToLower() : x.Key, 
                x => x.Value
            );
        }
    }
}
