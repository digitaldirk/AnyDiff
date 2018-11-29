﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using TypeSupport;
using TypeSupport.Extensions;

namespace AnyDiff
{
    /// <summary>
    /// Compare two objects for value differences
    /// </summary>
    public class DiffProvider
    {
        /// <summary>
        /// The default max depth to use
        /// </summary>
        internal const int DefaultMaxDepth = 32;

        /// <summary>
        /// The list of attributes to use when ignoring fields/properties
        /// </summary>
        private readonly ICollection<Type> _ignoreAttributes = new List<Type> {
            typeof(IgnoreDataMemberAttribute),
            typeof(NonSerializedAttribute),
            typeof(JsonIgnoreAttribute),
        };

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <param name="left">Object A</param>
        /// <param name="right">Object B</param>
        /// <param name="options">Specify the comparison options</param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff(object left, object right, ComparisonOptions options = ComparisonOptions.All)
        {
            return ComputeDiff(left, right, DefaultMaxDepth, options);
        }

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <param name="left">Object A</param>
        /// <param name="right">Object B</param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignorePropertiesOrPaths">A list of property names or full path names to ignore</param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff(object left, object right, ComparisonOptions options = ComparisonOptions.All, params string[] ignorePropertiesOrPaths)
        {
            return ComputeDiff(left, right, DefaultMaxDepth, options, ignorePropertiesOrPaths);
        }

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignoreProperties"></param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff<T>(T left, T right, ComparisonOptions options = ComparisonOptions.All, params Expression<Func<T, object>>[] ignoreProperties)
        {
            return ComputeDiff(left, right, DefaultMaxDepth, options, ignoreProperties);
        }

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <param name="maxDepth"></param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignorePropertiesOrPaths">A list of property names or full path names to ignore</param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff(object left, object right, int maxDepth, ComparisonOptions options = ComparisonOptions.All, params string[] ignorePropertiesOrPaths)
        {
            return RecurseProperties(left, right, null, new List<Difference>(), 0, maxDepth, new HashSet<int>(), false, string.Empty, options, ignorePropertiesOrPaths);
        }

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="left">Object A</param>
        /// <param name="right">Object B</param>
        /// <param name="maxDepth">Maximum recursion depth</param>
        /// <param name="options">Specify the comparison options</param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff<T>(T left, T right, int maxDepth, ComparisonOptions options = ComparisonOptions.All, params Expression<Func<T, object>>[] ignoreProperties)
        {
            var ignorePropertiesList = new List<string>();
            foreach (var expression in ignoreProperties)
            {
                var name = "";
                switch (expression.Body)
                {
                    case MemberExpression m:
                        name = m.Member.Name;
                        break;
                    case UnaryExpression u when u.Operand is MemberExpression m:
                        name = m.Member.Name;
                        break;
                    default:
                        throw new NotImplementedException(expression.GetType().ToString());
                }
                ignorePropertiesList.Add(name);
            }
            return RecurseProperties(left, right, null, new List<Difference>(), 0, maxDepth, new HashSet<int>(), false, string.Empty, options, ignorePropertiesList);
        }

        /// <summary>
        /// Compare two objects for value differences
        /// </summary>
        /// <param name="left">Object A</param>
        /// <param name="right">Object B</param>
        /// <param name="maxDepth">Maximum recursion depth</param>
        /// <param name="allowCompareDifferentObjects">True to allow comparison of objects of a different type</param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignorePropertiesOrPaths">A list of property names or full path names to ignore</param>
        /// <returns></returns>
        public ICollection<Difference> ComputeDiff(object left, object right, int maxDepth, bool allowCompareDifferentObjects, ComparisonOptions options = ComparisonOptions.All, params string[] ignorePropertiesOrPaths)
        {
            return RecurseProperties(left, right, null, new List<Difference>(), 0, maxDepth, new HashSet<int>(), allowCompareDifferentObjects, string.Empty, options, ignorePropertiesOrPaths);
        }

        /// <summary>
        /// Recurse the object's tree
        /// </summary>
        /// <param name="left">The left object to compare</param>
        /// <param name="right">The right object to compare</param>
        /// <param name="parent">The parent object</param>
        /// <param name="differences">A list of differences currently found in the tree</param>
        /// <param name="currentDepth">The current depth of the tree recursion</param>
        /// <param name="maxDepth">The maximum number of tree children to recurse</param>
        /// <param name="objectTree">A hash table containing the tree that has already been traversed, to prevent recursion loops</param>
        /// <param name="allowCompareDifferentObjects">True to allow comparing of objects with different types</param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignorePropertiesOrPaths">A list of property names or full path names to ignore</param>
        /// <returns></returns>
        private ICollection<Difference> RecurseProperties(object left, object right, object parent, ICollection<Difference> differences, int currentDepth, int maxDepth, HashSet<int> objectTree, bool allowCompareDifferentObjects, string path, ComparisonOptions options, ICollection<string> ignorePropertiesOrPaths = null)
        {
            if (!allowCompareDifferentObjects
                && left != null && right != null
                && left?.GetType() != right?.GetType())
                throw new ArgumentException("Objects Left and Right must be of the same type.");

            if (left == null && right == null)
                return differences;

            if (maxDepth > 0 && currentDepth >= maxDepth)
                return differences;

            if (ignorePropertiesOrPaths == null)
                ignorePropertiesOrPaths = new List<string>();

            var typeSupport = new ExtendedType(left != null ? left.GetType() : right.GetType());
            if (typeSupport.Attributes.Any(x => _ignoreAttributes.Contains(x)))
                return differences;
            if (typeSupport.IsDelegate)
                return differences;

            // increment the current recursion depth
            currentDepth++;

            // construct a hashtable of objects we have already inspected (simple recursion loop preventer)
            // we use this hashcode method as it does not use any custom hashcode handlers the object might implement
            if (left != null)
            {
                var hashCode = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(left);
                if (objectTree.Contains(hashCode))
                    return differences;
                objectTree.Add(hashCode);
            }

            // get list of properties
            var properties = new List<ExtendedProperty>();
            if(options.BitwiseHasFlag(ComparisonOptions.CompareProperties))
                properties.AddRange(left.GetProperties(PropertyOptions.All));

            // get all fields, except for backed auto-property fields
            var fields = new List<ExtendedField>();
            if (options.BitwiseHasFlag(ComparisonOptions.CompareFields))
            {
                fields.AddRange(left.GetFields(FieldOptions.All));
                fields = fields.Where(x => !x.IsBackingField).ToList();
            }

            var rootPath = path;
            foreach (var property in properties)
            {
                path = $"{rootPath}.{property.Name}";
                if (property.CustomAttributes.Any(x => _ignoreAttributes.Contains(x.AttributeType)))
                    continue;
                var propertyTypeSupport = new ExtendedType(property.Type);
                object leftValue = null;
                try
                {
                    if (left != null)
                        leftValue = left.GetPropertyValue(property);
                }
                catch (Exception)
                {
                    // catch any exceptions accessing the property
                }
                object rightValue = null;
                try
                {
                    if (right != null)
                        rightValue = right.GetPropertyValue(property);
                }
                catch (Exception)
                {
                    // catch any exceptions accessing the property
                }
                differences = GetDifferences(property.Name, property.Type, GetTypeConverter(property), leftValue, rightValue, parent, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
            }
            foreach (var field in fields)
            {
                path = $"{rootPath}.{field.Name}";
                if (field.CustomAttributes.Any(x => _ignoreAttributes.Contains(x.AttributeType)))
                    continue;
                var fieldTypeSupport = new ExtendedType(field.Type);
                object leftValue = null;
                if (left != null)
                    leftValue = left.GetFieldValue(field);
                object rightValue = null;
                if (right != null)
                    rightValue = right.GetFieldValue(field);
                differences = GetDifferences(field.Name, field.Type, GetTypeConverter(field), leftValue, rightValue, parent, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
            }

            return differences;
        }

        private TypeConverter GetTypeConverter(PropertyInfo property)
        {
            return GetTypeConverter(property.GetCustomAttributes(typeof(TypeConverterAttribute), false).FirstOrDefault() as TypeConverterAttribute);
        }

        private TypeConverter GetTypeConverter(FieldInfo property)
        {
            return GetTypeConverter(property.GetCustomAttributes(typeof(TypeConverterAttribute), false).FirstOrDefault() as TypeConverterAttribute);
        }

        private TypeConverter GetTypeConverter(TypeConverterAttribute attribute)
        {
            if (attribute != null)
            {
                var typeConverter = Activator.CreateInstance(Type.GetType(attribute.ConverterTypeName)) as TypeConverter;
                return typeConverter;
            }
            return null;
        }

        /// <summary>
        /// Get the differences between two objects
        /// </summary>
        /// <param name="propertyName">The name of the property being compared</param>
        /// <param name="propertyType">The type of property being compared. The left property is assumed unless allowCompareDifferentObjects=true</param>
        /// <param name="typeConverter">An optional TypeConverter to treat the type as a different type</param>
        /// <param name="left">The left object to compare</param>
        /// <param name="right">The right object to compare</param>
        /// <param name="parent">The parent object</param>
        /// <param name="differences">A list of differences currently found in the tree</param>
        /// <param name="currentDepth">The current depth of the tree recursion</param>
        /// <param name="maxDepth">The maximum number of tree children to recurse</param>
        /// <param name="objectTree">A hash table containing the tree that has already been traversed, to prevent recursion loops</param>
        /// <param name="allowCompareDifferentObjects">True to allow comparing of objects with different types</param>
        /// <param name="options">Specify the comparison options</param>
        /// <param name="ignorePropertiesOrPaths">A list of property names or full path names to ignore</param>
        /// <returns></returns>
        private ICollection<Difference> GetDifferences(string propertyName, Type propertyType, TypeConverter typeConverter, object left, object right, object parent, ICollection<Difference> differences, int currentDepth, int maxDepth, HashSet<int> objectTree, bool allowCompareDifferentObjects, string path, ComparisonOptions options, ICollection<string> ignorePropertiesOrPaths = null)
        {
            if (!ignorePropertiesOrPaths.Contains(propertyName) && !ignorePropertiesOrPaths.Contains(path))
            {
                object leftValue = null;
                object rightValue = null;
                leftValue = left;

                if (allowCompareDifferentObjects && rightValue != null)
                    rightValue = GetValueForProperty(right, propertyName);
                else
                    rightValue = right;

                if (rightValue == null && leftValue != null || leftValue == null && rightValue != null)
                {
                    differences.Add(new Difference((leftValue ?? rightValue).GetType(), propertyName, path, leftValue, rightValue, typeConverter));
                    return differences;
                }

                if (leftValue == null && rightValue == null)
                    return differences;

                var isCollection = propertyType != typeof(string) && propertyType.GetInterface(nameof(IEnumerable)) != null;
                if (isCollection && options.BitwiseHasFlag(ComparisonOptions.CompareCollections))
                {
                    // iterate the collection
                    var aValueCollection = (leftValue as IEnumerable);
                    var bValueCollection = (rightValue as IEnumerable);
                    var bValueEnumerator = bValueCollection?.GetEnumerator();
                    var arrayIndex = 0;
                    if (aValueCollection != null)
                    {
                        foreach (var collectionItem in aValueCollection)
                        {
                            var hasValue = bValueEnumerator?.MoveNext() ?? false;
                            leftValue = collectionItem;
                            if (hasValue)
                            {
                                rightValue = bValueEnumerator?.Current;
                                // check array element for difference
                                if (!leftValue.GetType().IsValueType && leftValue.GetType() != typeof(string))
                                {
                                    differences = RecurseProperties(leftValue, rightValue, parent, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
                                }
                                else if (leftValue.GetType().IsGenericType && leftValue.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                                {
                                    // compare keys and values of a KVP
                                    var leftKvpKey = GetValueForProperty(leftValue, "Key");
                                    var leftKvpValue = GetValueForProperty(leftValue, "Value");
                                    var rightKvpKey = GetValueForProperty(rightValue, "Key");
                                    var rightKvpValue = GetValueForProperty(rightValue, "Value");
                                    differences = RecurseProperties(leftKvpKey, rightKvpKey, leftValue, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
                                    differences = RecurseProperties(leftKvpValue, rightKvpValue, leftValue, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
                                }
                                else
                                {
                                    if (!IsMatch(leftValue, rightValue))
                                        differences.Add(new Difference(leftValue.GetType(), propertyName, path, arrayIndex, leftValue, rightValue, typeConverter));
                                }
                            }
                            else
                            {
                                // left has a value in collection, right does not. That's a difference
                                rightValue = null;
                                differences.Add(new Difference(leftValue.GetType(), propertyName, path, arrayIndex, leftValue, rightValue, typeConverter));
                            }
                            arrayIndex++;
                        }
                    }
                }
                else if (!propertyType.IsValueType && propertyType != typeof(string))
                {
                    differences = RecurseProperties(leftValue, rightValue, leftValue, differences, currentDepth, maxDepth, objectTree, allowCompareDifferentObjects, path, options, ignorePropertiesOrPaths);
                }
                else
                {
                    if (!IsMatch(leftValue, rightValue))
                        differences.Add(new Difference(propertyType, propertyName, path, leftValue, rightValue, typeConverter));
                }
            }
            return differences;
        }

        private object GetValueForProperty(object obj, string propertyName)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            var property = obj.GetType().GetProperties().FirstOrDefault(x => x.Name.Equals(propertyName));
            return property?.GetValue(obj);
        }

        private object GetValueForField(object obj, string propertyName)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            var field = obj.GetType().GetFields().FirstOrDefault(x => x.Name.Equals(propertyName));
            return field?.GetValue(obj);
        }

        private bool IsMatch(object leftValue, object rightValue)
        {
            var isMatch = false;
            if (leftValue == null && rightValue == null)
            {
                isMatch = true;
            }
            else if (leftValue == null || rightValue == null)
            {
                isMatch = false;
            }
            else if (leftValue.Equals(rightValue))
            {
                isMatch = true;
            }
            return isMatch;
        }
    }
}
