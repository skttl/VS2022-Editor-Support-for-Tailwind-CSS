﻿using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.Configuration
{
    /// <summary>
    /// Parses the TailwindCSS configuration file
    /// </summary>
    [Export]
    internal sealed class ConfigFileParser
    {
        [Import]
        internal ConfigFileScanner Scanner { get; set; }

        internal async Task<JsonObject> GetConfigJsonNodeAsync()
        {
            var path = await Scanner.FindConfigurationFilePathAsync();

            if (path is null)
            {
                return null;
            }

            var processInfo = new ProcessStartInfo()
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                FileName = "cmd",
                Arguments = "/c node",
                WorkingDirectory = Path.GetDirectoryName(path)
            };

            using var process = Process.Start(processInfo);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            string command;

            command = $@"(function () {{
    (async function () {{
        var Module = require('module');
        var originalRequire = Module.prototype.require;

        Module.prototype.require = function () {{
            if (arguments[0] === 'tailwindcss/plugin') {{
                return (function () {{
                    function withOptions(pluginDetails, pluginExports) {{
                        return function (value) {{
                            return {{
                                handler: function (functions) {{
                                    options = value;
                                    return pluginDetails(value)(functions);
                                }},
                                config: (pluginExports && typeof pluginExports === 'function') ? pluginExports(value) : {{}}
                            }}
                        }};
                    }}
                    function main(setup, configuration = {{}}) {{
                        return function (value) {{
                            return {{
                                handler: function (functions) {{
                                    return setup(functions);
                                }},
                                config: configuration
                            }};
                        }};
                    }}
                    main.withOptions = withOptions;
                    return main;
                }})();
            }}

            return originalRequire.apply(this, arguments);
        }};

        var configuration;

        try {{
            configuration = require('./{Path.GetFileName(path)}');
        }} catch {{
            configuration = await import('././{Path.GetFileName(path)}');
        }}

        if (configuration.default) {{
            configuration = configuration.default;
        }}
        function getValueByKeyBracket(object, key) {{
            const keys = key.split('.');

            const result = keys.reduce((acc, currentKey) => {{
                if (acc && typeof acc === 'object' && currentKey in acc) {{
                    return acc[currentKey];
                }}
                return undefined;
            }}, object);

            return result;
        }}

        if (configuration.plugins) {{
            var pluginTheme = {{ theme: configuration.theme }};
            var newPlugins = [];
            configuration.plugins.reverse().forEach(function (plugin) {{
                if (typeof plugin === 'function') {{
                    try {{
                        var evaluated = plugin({{}});

                        if (evaluated && evaluated.handler && evaluated.config) {{
                            plugin = evaluated;
                        }}
                    }} catch {{

                    }}
                }}
                if (plugin && plugin.handler && plugin.config) {{
                    if (!pluginTheme) {{
                        pluginTheme = {{}};
                    }}

                    Object.keys(plugin.config).forEach(key => {{
                        pluginTheme[key] = {{ ...pluginTheme[key], ...plugin.config[key] }};
                    }});

                    newPlugins.push(plugin.handler);
                }} else {{
                    newPlugins.push(plugin);
                }}
            }});

            configuration = {{
                ...configuration,
                ...pluginTheme
            }};

            configuration.plugins = newPlugins;
        }}

        function theme(key, defaultValue) {{
            const defaultTheme = require('tailwindcss/defaultTheme');
            const colors = require('tailwindcss/colors');
            const custom = configuration;

            let defaultThemeValue = getValueByKeyBracket(defaultTheme, key);

            // Default theme may contain functions, so we should provide that
            if (typeof defaultThemeValue === 'function') {{
                console.log(theme);
                defaultThemeValue = defaultThemeValue({{ theme: theme, colors: colors }});
            }}

            const output = {{
                ...defaultThemeValue,
                ...getValueByKeyBracket(custom.theme, key),
                ...getValueByKeyBracket(custom.theme.extend, key)
            }};
            
            return (!output || Object.keys(output).length === 0) ? defaultValue : output;
        }}

        const defaultLog = console.log;
        console.log = function () {{ }}

        var parsed = JSON.stringify(configuration,
            (key, value) => {{
                if (key === 'plugins') {{
                    var classes = [];
                    var modifiers = [];
                    value.forEach(function (p) {{
                        p({{
                            theme: theme,
                            config: (key, defaultValue) => {{
                                return getValueByKeyBracket(configuration, key) || defaultValue;
                            }},
                            addUtilities: (utilities, options = null) => {{
                                if (utilities) {{
                                    if (typeof utilities[Symbol.iterator] === 'function') {{
                                        utilities.forEach(function (u) {{
                                            classes.push(...Object.keys(u));
                                        }})
                                    }} else {{
                                        classes.push(...Object.keys(utilities));
                                    }}
                                }}
                            }},
                            matchUtilities: (utilities, {{ values, supportsNegativeValues }} = null) => {{
                                if (utilities) {{
                                    if (values) {{
                                        for (const v of Object.entries(values)) {{
                                            for (const u of Object.entries(utilities)) {{
                                                var input = `${{u[0]}}-${{v[0]}}`.replace('-DEFAULT', '');
                                                classes.push(input);
                                                if (supportsNegativeValues === true) {{
                                                    classes.push(`-${{input}}`);
                                                }}
                                            }}
                                        }}
                                    }}

                                    for (const u of Object.entries(utilities)) {{
                                        classes.push(`${{u[0]}}-[]`);
                                        if (supportsNegativeValues === true) {{
                                            classes.push(`-${{u[0]}}-[]`);
                                        }}
                                    }}
                                }}
                            }},
                            addComponents: (components, options = null) => {{
                                if (components) {{
                                    if (typeof components[Symbol.iterator] === 'function') {{
                                        components.forEach(function (c) {{
                                            classes.push(...Object.keys(c));
                                        }})
                                    }} else {{
                                        classes.push(...Object.keys(components));
                                    }}
                                }}
                            }},
                            matchComponents: (components, {{ values, supportsNegativeValues }} = null) => {{
                                if (components) {{
                                    if (values) {{
                                        for (const v of Object.entries(values)) {{
                                            for (const u of Object.entries(components)) {{
                                                var input = `${{u[0]}}-${{v[0]}}`.replace('-DEFAULT', '');
                                                classes.push(input);
                                                if (supportsNegativeValues === true) {{
                                                    classes.push(`-${{input}}`);
                                                }}
                                            }}
                                        }}
                                    }}

                                    for (const u of Object.entries(components)) {{
                                        classes.push(`${{u[0]}}-[]`);
                                        if (supportsNegativeValues === true) {{
                                            classes.push(`-${{u[0]}}-[]`);
                                        }}
                                    }}
                                }}
                            }},
                            addBase: (base) => {{
                                return;
                            }},
                            addVariant: (name, value) => {{
                                modifiers.push(name)
                            }},
                            matchVariant: (name, cb, {{ values }} = null) => {{
                                if (name !== '@') {{
                                    name += '-';
                                }}
                                if (values) {{
                                    for (const v of Object.entries(values)) {{
                                        modifiers.push(`${{name}}${{v[0].replace('DEFAULT', '')}}`);
                                    }}
                                }}

                                modifiers.push(`${{name}}[]`);
                            }},
                            corePlugins: (path) => {{
                                return configuration.corePlugins[path] !== false;
                            }},
                            e: (className) => {{
                                return className.replace(/[!@#$%^&*(),.?"":{{}}|<> ]/g, '\\$&');
                            }},
                            prefix: (className) => {{
                                return '.' + (configuration.prefix ? '' : configuration.prefix) + className.replace('.', '');
                            }}
                        }});
                    }});

                    return {{
                        'classes': classes,
                        'modifiers': modifiers
                    }};
                }} else {{
                    return typeof value === 'function' ? value({{
                        theme: theme
                    }}) : value;
                }}
            }}
        );
        console.log = defaultLog;
        console.log(parsed);
    }})()
}})();";

            var file = new StringBuilder();

            process.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                if (string.IsNullOrEmpty(e.Data) == false)
                {
                    file.AppendLine(e.Data);
                }
            };
            var hasError = false;
            var error = new StringBuilder();
            process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                if (string.IsNullOrEmpty(e.Data) == false && e.Data.Contains("warn") == false)
                {
                    hasError = true;
                    error.AppendLine(e.Data);
                }
            };
            await process.StandardInput.WriteLineAsync(command);
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            if (hasError)
            {
                throw new InvalidOperationException("Error occurred while parsing configuration file: " + error.ToString().Trim());
            }

            var fileText = file.ToString().Trim();

            return JsonSerializer.Deserialize<JsonObject>(fileText);
        }

        /// <summary>
        /// Gets the configuration settings from the TailwindCSS configuration file.
        /// </summary>
        /// <remarks>Returns null if the configuration file cannot be found, or if the configuration file does not have a 'theme' section.</remarks>
        /// <returns>Returns a <see cref="Task{TailwindConfiguration}" /> of type <see cref="TailwindConfiguration"/> which contains the parsed configuration information</returns>
        internal async Task<TailwindConfiguration> GetConfigurationAsync()
        {
            var obj = await GetConfigJsonNodeAsync();

            if (obj.Count == 1 && obj.ContainsKey("default"))
            {
                obj = obj["default"].AsObject();
            }

            var theme = obj["theme"];

            if (theme == null)
            {
                return null;
            }

            var plugins = GetTotalValue(obj["plugins"]) ?? [];

            var config = new TailwindConfiguration
            {
                OverridenValues = GetTotalValue(theme, "extend") ?? new Dictionary<string, object>(),
                ExtendedValues = GetTotalValue(theme["extend"]) ?? new Dictionary<string, object>(),
                Prefix = obj["prefix"]?.ToString(),
            };

            try
            {
                config.PluginModifiers = plugins.ContainsKey("modifiers") ? (List<string>)plugins["modifiers"] : null;

                if (plugins.ContainsKey("classes"))
                {
                    config.PluginClasses = new List<string>();
                    var classes = (List<string>)plugins["classes"];

                    foreach (var item in classes)
                    {
                        if (item.Contains("@media") || item.Contains("@font-face") || item.Contains("@keyframes") || item.Contains("@supports"))
                        {
                            continue;
                        }
                        var commaSplitClasses = item.Split(new string[] { ",", " .", "." }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var className in commaSplitClasses)
                        {
                            string toAdd = className.Trim();
                            if (toAdd.StartsWith("[") && toAdd.EndsWith("]"))
                            {
                                continue;
                            }
                            if (toAdd.Contains('[') && toAdd.IndexOf('[') != toAdd.IndexOf("-[") + 1)
                            {
                                toAdd = toAdd.Substring(0, toAdd.IndexOf('['));
                            }
                            if (toAdd.Contains(':'))
                            {
                                toAdd = toAdd.Substring(0, toAdd.IndexOf(':'));
                            }
                            if (toAdd.Contains(' '))
                            {
                                toAdd = toAdd.Substring(0, toAdd.IndexOf(' '));
                            }

                            toAdd = toAdd.TrimEnd(')');

                            if (config.PluginClasses.Contains(toAdd) == false && string.IsNullOrEmpty(toAdd) == false)
                            {
                                config.PluginClasses.Add(toAdd);
                            }
                        }
                    }
                }
                else
                {
                    config.PluginClasses = null;
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }

            return config;
        }

        private Dictionary<string, object> GetTotalValue(JsonNode node, string ignoreKey = null)
        {
            if (node == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>();

            if (GetValueKind(node) == JsonValueKind.Object)
            {
                foreach (var key in GetKeys(node))
                {
                    if (key == ignoreKey)
                    {
                        continue;
                    }
                    var valueKind = GetValueKind(node[key]);
                    if (valueKind == JsonValueKind.Object)
                    {
                        result[key] = GetTotalValue(node[key]);
                    }
                    else if (valueKind == JsonValueKind.Array)
                    {
                        result[key] = node[key].AsArray().Select(n => n.ToString()).ToList();
                    }
                    else if (valueKind != JsonValueKind.Null)
                    {
                        result[key] = node[key].ToString().Trim();
                    }
                }
            }

            return result;
        }

        private ICollection<string> GetKeys(JsonNode obj)
        {
            return ((IDictionary<string, JsonNode>)obj).Keys;
        }

        private JsonValueKind GetValueKind(JsonNode node)
        {
            if (node is JsonObject)
            {
                return JsonValueKind.Object;
            }
            else if (node is JsonArray)
            {
                return JsonValueKind.Array;
            }
            else if (node is null)
            {
                return JsonValueKind.Null;
            }

            var value = node.GetValue<JsonElement>();

            return value.ValueKind;
        }
    }
}
