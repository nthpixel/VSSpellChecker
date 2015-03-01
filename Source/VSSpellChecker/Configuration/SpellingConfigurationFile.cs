﻿//===============================================================================================================
// System  : Visual Studio Spell Checker Package
// File    : SpellingConfigurationFile.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 02/28/2015
// Note    : Copyright 2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the class used to load and save spell checker configuration files
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/VSSpellChecker
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
//===============================================================================================================
// 02/01/2015  EFW  Refactored configuration settings
//===============================================================================================================

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace VisualStudio.SpellChecker.Configuration
{
    /// <summary>
    /// This class is used to load and save spell checker configuration files
    /// </summary>
    public class SpellingConfigurationFile
    {
        #region Private data members
        //=====================================================================

        private Dictionary<string, PropertyInfo> propertyCache;
        private PropertyDescriptorCollection configCache, csoCache, cadCache;
        private SpellCheckerConfiguration defaultConfig;

        private XDocument document;
        private XElement root;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property returns the global configuration file path
        /// </summary>
        /// <value>This location is also where custom dictionary files are located</value>
        public static string GlobalConfigurationFilePath
        {
            get
            {
                string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"EWSoftware\Visual Studio Spell Checker");

                if(!Directory.Exists(configPath))
                    Directory.CreateDirectory(configPath);

                return configPath;
            }
        }

        /// <summary>
        /// This is used to return the global configuration filename (VSSpellChecker.vsspell)
        /// </summary>
        /// <value>If the legacy configuration file exists, its name will be returned instead.  It will be
        /// converted and removed the next time it is opened for editing.</value>
        public static string GlobalConfigurationFilename
        {
            get
            {
                string legacyConfig = Path.Combine(GlobalConfigurationFilePath, "SpellChecker.config");

                if(File.Exists(legacyConfig))
                    return legacyConfig;

                return Path.Combine(GlobalConfigurationFilePath, "VSSpellChecker.vsspell");
            }
        }

        /// <summary>
        /// This property is used to get or set the filename
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// This read-only property returns the configuration file type
        /// </summary>
        /// <value>Configuration files are associated with filenames and the type is determined by examining the
        /// filename.</value>
        public ConfigurationType ConfigurationType
        {
            get
            {
                string filename = this.Filename;

                if(String.IsNullOrWhiteSpace(filename))
                    return ConfigurationType.File;

                if(filename.Equals(GlobalConfigurationFilename))
                    return ConfigurationType.Global;

                string relatedFile = Path.GetFileNameWithoutExtension(filename),
                    folder = Path.GetDirectoryName(filename);

                if(folder.EndsWith(relatedFile, StringComparison.OrdinalIgnoreCase))
                    return ConfigurationType.Folder;

                if(relatedFile.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                    return ConfigurationType.Solution;

                if(Path.GetExtension(relatedFile).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                    return ConfigurationType.Project;

                return ConfigurationType.File;
            }
        }
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="filename">The filename to use</param>
        /// <param name="defaultConfig">A default configuration to use for missing properties</param>
        public SpellingConfigurationFile(string filename, SpellCheckerConfiguration defaultConfig)
        {
            if(String.IsNullOrWhiteSpace(filename))
                throw new ArgumentNullException("filename", "Filename cannot be null or empty");

            try
            {
                // Get the property cache for finding current and default values
                propertyCache = new Dictionary<string, PropertyInfo>();
                configCache = TypeDescriptor.GetProperties(typeof(SpellCheckerConfiguration));
                csoCache = TypeDescriptor.GetProperties(typeof(CSharpOptions));
                cadCache = TypeDescriptor.GetProperties(typeof(CodeAnalysisDictionaryOptions));

                foreach(PropertyInfo property in typeof(SpellCheckerConfiguration).GetProperties(
                  BindingFlags.Public | BindingFlags.Instance))
                    propertyCache.Add(property.Name, property);

                foreach(PropertyInfo property in typeof(CSharpOptions).GetProperties(
                  BindingFlags.Public | BindingFlags.Instance))
                    propertyCache.Add(property.Name, property);

                foreach(PropertyInfo property in typeof(CodeAnalysisDictionaryOptions).GetProperties(
                  BindingFlags.Public | BindingFlags.Instance))
                    propertyCache.Add(property.Name, property);
            }
            catch
            {
                // Ignore exceptions
            }

            this.Filename = filename;
            this.defaultConfig = defaultConfig;

            if(File.Exists(filename))
            {
                document = XDocument.Load(filename);
                root = document.Root;

                // If it's an older configuration file, upgrade it to the new format
                if(root.Attribute("Format") == null || root.Attribute("Format").Value != AssemblyInfo.ConfigSchemaVersion)
                    this.UpgradeConfiguration();
            }
            else
            {
                root = new XElement("SpellCheckerConfiguration", new XAttribute("Format",
                    AssemblyInfo.ConfigSchemaVersion));

                document = new XDocument(new XComment(" Visual Studio Spell Checker configuration file - " +
                    "[https://github.com/EWSoftware/VSSpellChecker]\r\n     Do not edit the XML.  Use the " +
                    "configuration file editor in Visual Studio to modify the settings. "), root);
            }
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// Upgrade an older configuration file to the latest format
        /// </summary>
        private void UpgradeConfiguration()
        {
            string[] propertyNames = new[] { PropertyNames.SpellCheckAsYouType,
                PropertyNames.IgnoreWordsWithDigits, PropertyNames.IgnoreWordsInAllUppercase,
                PropertyNames.IgnoreFormatSpecifiers, PropertyNames.IgnoreFilenamesAndEMailAddresses,
                PropertyNames.IgnoreXmlElementsInText, PropertyNames.TreatUnderscoreAsSeparator };

            // So far, there's only one old format
            document.AddFirst(new XComment(" Visual Studio Spell Checker configuration file - " +
                "[https://github.com/EWSoftware/VSSpellChecker]\r\n     Do not edit the XML.  Use the " +
                "configuration file editor in Visual Studio to modify the settings. "));

            root.Add(new XAttribute("Format", AssemblyInfo.ConfigSchemaVersion));

            // Set values on these elements
            foreach(string name in propertyNames)
            {
                var property = root.Element(name);

                if(property != null && String.IsNullOrWhiteSpace(property.Value))
                    property.Value = "True";
            }

            // Move the C# options into the parent element
            XElement ignoreXmlDocComments = root.Element("IgnoreXmlDocComments"),
                ignoreDelimitedComments = root.Element("IgnoreDelimitedComments"),
                ignoreStandardSingleLineComments = root.Element("IgnoreStandardSingleLineComments"),
                ignoreQuadrupleSlashComments = root.Element("IgnoreQuadrupleSlashComments"),
                ignoreNormalStrings = root.Element("IgnoreNormalStrings"),
                ignoreVerbatimStrings = root.Element("IgnoreVerbatimStrings"),
                ignoredCharacterClass = root.Element("IgnoredCharacterClass"),
                csharpOptions = new XElement("CSharpOptions");

            if(ignoreXmlDocComments != null)
            {
                ignoreXmlDocComments.Value = "True";
                ignoreXmlDocComments.Remove();
                csharpOptions.Add(ignoreXmlDocComments);
            }

            if(ignoreDelimitedComments != null)
            {
                ignoreDelimitedComments.Value = "True";
                ignoreDelimitedComments.Remove();
                csharpOptions.Add(ignoreDelimitedComments);
            }

            if(ignoreStandardSingleLineComments != null)
            {
                ignoreStandardSingleLineComments.Value = "True";
                ignoreStandardSingleLineComments.Remove();
                csharpOptions.Add(ignoreStandardSingleLineComments);
            }

            if(ignoreQuadrupleSlashComments != null)
            {
                ignoreQuadrupleSlashComments.Value = "True";
                ignoreQuadrupleSlashComments.Remove();
                csharpOptions.Add(ignoreQuadrupleSlashComments);
            }

            if(ignoreNormalStrings != null)
            {
                ignoreNormalStrings.Value = "True";
                ignoreNormalStrings.Remove();
                csharpOptions.Add(ignoreNormalStrings);
            }

            if(ignoreVerbatimStrings != null)
            {
                ignoreVerbatimStrings.Value = "True";
                ignoreVerbatimStrings.Remove();
                csharpOptions.Add(ignoreVerbatimStrings);
            }

            if(csharpOptions.HasElements)
                root.Add(csharpOptions);

            // Rename the ignored character class element
            if(ignoredCharacterClass != null)
            {
                ignoredCharacterClass.Remove();
                root.Add(new XElement(PropertyNames.IgnoreCharacterClass, ignoredCharacterClass.Value));
            }

            // Convert excluded extensions to a list
            var excludeExts = root.Element("ExcludeByFilenameExtension");

            if(excludeExts != null)
            {
                excludeExts.Remove();

                string excluded = excludeExts.Value;

                if(!String.IsNullOrWhiteSpace(excluded))
                {
                    excludeExts = new XElement(PropertyNames.ExcludedExtensions);
                    root.Add(excludeExts);

                    foreach(string ext in excluded.Split(new[] { ',', ' ', '\t', '\r', '\n' },
                      StringSplitOptions.RemoveEmptyEntries))
                    {
                        string addExt;

                        if(ext[0] != '.')
                            addExt = "." + ext;
                        else
                            addExt = ext;

                        excludeExts.Add(new XElement(PropertyNames.ExcludedExtensionsItem, addExt));
                    }
                }
            }
        }

        /// <summary>
        /// Get the default value for the specified property
        /// </summary>
        /// <param name="propertyName">The property name</param>
        /// <returns>The default value if the property is found or null if not found and no default value
        /// attribute is defined.</returns>
        private object DefaultValueFor(string propertyName)
        {
            if(configCache != null && csoCache != null && cadCache != null && propertyCache != null)
                try
                {
                    string[] parts = propertyName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                    PropertyInfo property = null;
                    object propertyValue = defaultConfig;

                    // Try to get the value from the default configuration if there is one
                    if(propertyValue != null)
                        foreach(string name in parts)
                        {
                            if(!propertyCache.TryGetValue(name, out property))
                            {
                                property = null;
                                break;
                            }

                            propertyValue = property.GetValue(propertyValue, null);
                        }

                    if(property != null)
                        return propertyValue;

                    // If not found, get it from the default value attribute if defined
                    var prop = configCache[parts[parts.Length - 1]];

                    if(prop == null)
                    {
                        prop = csoCache[parts[parts.Length - 1]];

                        if(prop == null)
                        {
                            prop = cadCache[parts[parts.Length - 1]];

                            if(prop == null)
                                return null;
                        }
                    }

                    var defValue = prop.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;

                    return (defValue != null) ? defValue.Value : null;
                }
                catch
                {
                    // Ignore errors retrieving values
                }

            return null;
        }

        /// <summary>
        /// Get a property element from the XML document
        /// </summary>
        /// <param name="propertyName">The property name</param>
        /// <returns>Returns the property name if found, or null if not.  If the property name contains dot
        /// separators, the requested nested element is retrieved.</returns>
        private XElement GetPropertyElement(string propertyName)
        {
            string[] elementNames = propertyName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            XElement property = null, current = root;

            foreach(string name in elementNames)
            {
                property = current.Element(name);

                if(property == null)
                    break;

                current = property;
            }

            return property;
        }


        /// <summary>
        /// Get a property element from the XML document or create it if it does not exist
        /// </summary>
        /// <param name="propertyName">The property name</param>
        /// <returns>Returns the property name if found, or the created property if not.  If the property name
        /// contains dot separators, the requested nested element is retrieved.</returns>
        private XElement GetOrCreatePropertyElement(string propertyName)
        {
            string[] elementNames = propertyName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            XElement property = null, current = root;

            foreach(string name in elementNames)
            {
                property = current.Element(name);

                if(property == null)
                {
                    property = new XElement(name);
                    current.Add(property);
                    current = property;
                }
                else
                    current = property;
            }

            return property;
        }

        /// <summary>
        /// This is used to see if the configuration file contains the named property
        /// </summary>
        /// <param name="propertyName">The property name for which to check</param>
        /// <returns>Returns true if the property name is found, or false if not.  If the property name contains
        /// dot separators, the requested nested element is checked for existence.</returns>
        public bool HasProperty(string propertyName)
        {
            return (this.GetPropertyElement(propertyName) != null);
        }

        /// <summary>
        /// Convert a configuration element to a new <see cref="String"/> instance or return the default value
        /// </summary>
        /// <param name="propertyName">The property name to retrieve</param>
        /// <returns>The requested <c>String</c> value or the default if not found</returns>
        public string ToString(string propertyName)
        {
            var property = this.GetPropertyElement(propertyName);

            if(property != null && !String.IsNullOrWhiteSpace(property.Value))
                return property.Value;

            return (string)this.DefaultValueFor(propertyName);
        }

        /// <summary>
        /// Convert a configuration element to a new <see cref="Boolean"/> instance or return the default value
        /// </summary>
        /// <param name="propertyName">The property name to retrieve</param>
        /// <returns>The requested <c>Boolean</c> value or the default if not found</returns>
        public bool ToBoolean(string propertyName)
        {
            bool value;
            var property = this.GetPropertyElement(propertyName);

            if(property == null || String.IsNullOrWhiteSpace(property.Value) ||
              !Boolean.TryParse(property.Value, out value))
            {
                object defaultValue = this.DefaultValueFor(propertyName);
                return (defaultValue != null) ? (bool)defaultValue : false;
            }

            return value;
        }

        /// <summary>
        /// Convert a configuration element to a new <see cref="CultureInfo"/> instance or return the default
        /// value.
        /// </summary>
        /// <param name="propertyName">The property name to retrieve</param>
        /// <returns>The requested <c>CultureInfo</c> value or the default if not found</returns>
        public CultureInfo ToCultureInfo(string propertyName)
        {
            var property = this.GetPropertyElement(propertyName);

            if(property == null || String.IsNullOrWhiteSpace(property.Value))
            {
                object defaultValue = this.DefaultValueFor(propertyName);
                return (defaultValue != null) ? (CultureInfo)defaultValue : new CultureInfo("en-US");
            }

            return new CultureInfo(property.Value);
        }

        /// <summary>
        /// Convert a configuration element to a new instance of the specified enumeration type or return the
        /// default value.
        /// </summary>
        /// <param name="propertyName">The property name to retrieve</param>
        /// <param name="defaultValue">The default value to use if not present</param>
        /// <returns></returns>
        public TEnum ToEnum<TEnum>(string propertyName) where TEnum : struct
        {
            TEnum value;
            var property = this.GetPropertyElement(propertyName);

            if(property == null || String.IsNullOrWhiteSpace(property.Value) ||
              !Enum.TryParse<TEnum>(property.Value, true, out value))
            {
                object defaultValue = this.DefaultValueFor(propertyName);
                value = (defaultValue != null) ? (TEnum)defaultValue : default(TEnum);
            }

            return value;
        }

        /// <summary>
        /// Convert a configuration element to an enumerable list of string values or return an empty enumeration
        /// if not found.
        /// </summary>
        /// <param name="propertyName">The property name to retrieve</param>
        /// <param name="valueName">The value element name of the sub-elements within the parent property</param>
        /// <returns></returns>
        public IEnumerable<string> ToValues(string propertyName, string valueName)
        {
            var property = this.GetPropertyElement(propertyName);

            if(property != null && property.HasElements)
                foreach(var value in property.Descendants(valueName))
                    if(!String.IsNullOrWhiteSpace(value.Value))
                        yield return value.Value;
        }

        /// <summary>
        /// Store a property in the configuration file or remove it if the value is null
        /// </summary>
        /// <param name="prpertyName">The property name</param>
        /// <param name="value">The property value</param>
        public void StoreProperty(string propertyName, object value)
        {
            XElement property;

            if(value != null)
            {
                property = this.GetOrCreatePropertyElement(propertyName);
                property.Value = value.ToString();
            }
            else
            {
                property = this.GetPropertyElement(propertyName);

                if(property != null)
                    property.Remove();
            }
        }

        /// <summary>
        /// Store an enumerable list of value in the configuration file or removes it if the list is null
        /// </summary>
        /// <param name="prpertyName">The property name that will contain the items</param>
        /// <param name="itemName">The item name for the value elements</param>
        /// <param name="values">The enumerable list of values</param>
        public void StoreValues(string propertyName, string itemName, IEnumerable<string> values)
        {
            XElement property;

            if(values != null)
            {
                property = this.GetOrCreatePropertyElement(propertyName);

                if(property.HasElements)
                    property.RemoveNodes();

                property.Add(values.Select(v => new XElement(itemName) { Value = v }));
            }
            else
            {
                property = this.GetPropertyElement(propertyName);

                if(property != null)
                    property.Remove();
            }
        }

        /// <summary>
        /// This is used to save the spell checker configuration settings
        /// </summary>
        public bool Save()
        {
            bool success = true;

            try
            {
                document.Save(this.Filename);
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                success = false;
            }

            return success;
        }
        #endregion
    }
}
