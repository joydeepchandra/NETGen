using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using NETGen.Core;

namespace NETGen.pyspg
{
	public abstract class pyspgSimulation<T> where T : pyspgSimulation<T>, new()
	{		
		private static string scriptname = "";
		
		public static void Init(string[] args)
		{
			//create instance of the derived type
			T simulation = new T();
			scriptname = "runsim_" + System.AppDomain.CurrentDomain.FriendlyName.Replace(".exe", "");
			
			if(args.Length==1 && args[0] == "-g")				
			{
				simulation.BuildConfigFiles();
			}
			// Perform the run for a given input parameter file
			else if (args.Length==2 && (args[0] == "-i" || args[0]=="-d") && System.IO.File.Exists(args[1]))
			{				
				// Set logger to data recording mode
				if(args[0]=="-i")
				{
					Logger.ShowAppMsg = false;
					Logger.ShowErrors = false;
					Logger.ShowInfos = false;
					Logger.ShowSimMsg = false;
					Logger.ShowWarnings = false;
				}
				
				// set values of parameters to values according to input file
				simulation.SetParameters(args[1]);
				
				// Run the user provided simulation
				simulation.RunSimulation();
				
				// Print tab-separated output values to stdout
				foreach(FieldInfo fi in GetFields(simulation.GetType(), typeof(ParameterAttribute)))
					if( (fi.GetCustomAttributes(typeof(ParameterAttribute), true)[0] as ParameterAttribute).Type == ParameterType.Output )
						Console.Write(fi.GetValue(simulation).ToString()+"\t");			
				Console.Write("\n");
			}
			// Check if the values can be set correctly 
			else if (args.Length==2 && args[0] == "-c")
			{
				if(!System.IO.File.Exists(args[1]))
					Logger.AddMessage(LogEntryType.Error, "Input parameters file does not exist.");
				
				try {					
					simulation.SetParameters(args[1]);
					Logger.AddMessage(LogEntryType.AppMsg, "Input file ok.");
				}
				catch (Exception ex) {
					Logger.AddMessage(LogEntryType.Error, "Setting input parameters failed." + ex.Message);
				}
			}
			else
			{
				Logger.AddMessage(LogEntryType.Info, "Usage: \t" + System.AppDomain.CurrentDomain.FriendlyName + " [options]");
				Logger.AddMessage(LogEntryType.Info, "\t -g \t Generate pyspg config files for this simulation");
				Logger.AddMessage(LogEntryType.Info, "\t -c \t[filename]\t Check whether the input parameter file is correct for this simulation");
				Logger.AddMessage(LogEntryType.Info, "\t -i \t[filename]\t Run simulation for the given input parameter file");
				Logger.AddMessage(LogEntryType.Info, "\t -d \t[filename]\t Run single simulation for the given input parameter file with full debug output");
			}
		}
		
		/// <summary>
		/// Creates a value based on the field type and a string-encoded value
		/// </summary>
		/// <returns>
		/// The value.
		/// </returns>
		/// <param name='simulationType'>
		/// Simulation type.
		/// </param>
		/// <param name='paramname'>
		/// Paramname.
		/// </param>
		/// <param name='paramvalue'>
		/// Paramvalue.
		/// </param>
		object CreateValue (Type simulationType, string paramname, string paramvalue)
		{
			if(simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType == typeof(int))
				return Int32.Parse(paramvalue);
			else if(simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType == typeof(long))
				return Int64.Parse(paramvalue);
			else if (simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType == typeof(double))
				return double.Parse(paramvalue);
			else if (simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType == typeof(float))
				return float.Parse(paramvalue);
			else if (simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType.IsEnum)				
				return Enum.Parse(simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType, paramvalue);
			else if (simulationType.GetField(paramname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).FieldType == typeof(string))
				return paramvalue;
			return null;
		}
		
		/// <summary>
		/// Sets the input parameter fields of the instance to the parameters given in the input file
		/// </summary>
		/// <param name='inputFile'>
		/// Input file.
		/// </param>
		private void SetParameters(string inputFile)
		{
			Type simulationType = this.GetType();
			
			Dictionary<string, object> values = new Dictionary<string, object>();
			
			string[] lines = System.IO.File.ReadAllLines(inputFile);
			
			foreach(string s in lines)
			{
				string paramname = s.Split(' ')[0]; 
				if(paramname.StartsWith("store_") && paramname.EndsWith("_filename"))
				{
					paramname = paramname.Replace("store_", "").Replace("_filename", "");
				}
				string paramvalue = s.Split(' ')[1]; 
				values[paramname] = CreateValue(simulationType, paramname, paramvalue);
			}

			foreach(FieldInfo fi in simulationType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
				if(values.ContainsKey(fi.Name))
					fi.SetValue(this, values[fi.Name]);
		}

		static void BuildStartupScript (string filename)
		{
			string output= "#!/bin/bash\n";
			output += "mono-sgen ~/opt/bin/" + System.AppDomain.CurrentDomain.FriendlyName + " $1 $2 $3";
			
			System.IO.File.WriteAllText(filename, output);
		}

		static void BuildCTFile (Dictionary<string, FieldInfo> inputfields, Dictionary<string, ParameterAttribute> fieldAttributes, string filename)
		{
			string output = "";
			foreach(string s in inputfields.Keys)
			{
				if(output!="")
						output += "\n";
				
				if(fieldAttributes[s].Type == ParameterType.OutputFile)
				{
					output += "@conditional_file_out "	 + inputfields[s].Name; 
				}
				else
				{
					// Add type code
					output += TranslateType(inputfields[s].FieldType);
					
					// Add parameter name
					output += ":"+ inputfields[s].Name;
					
					// Add possible choices
					if (inputfields[s].FieldType.IsEnum)
					{
						bool first = true;
						foreach(string e in inputfields[s].FieldType.GetEnumNames())
						{
							if(first)
							{
								output +=":";
								first = false;
							}
							else
								output += ",";
							
							output += "\"" + e + "\"";
						}
					}
					// Add default value
					else 
						output += ":" + (fieldAttributes[s].DefaultValue!=null?fieldAttributes[s].DefaultValue.ToString():"");
					
					// Add help string
					output += ":" + fieldAttributes[s].Comment;
				}
			}
			System.IO.File.WriteAllText(filename, output);
		}

		static void BuildStdOutFile (Dictionary<string, FieldInfo> outputfields, Dictionary<string, ParameterAttribute> fieldAttributes, string filename)
		{
			string output = "";			
			foreach(string s in outputfields.Keys)
			{
				if(output!="")
					output += "\n";
				
				output += outputfields[s].Name +":help = " + fieldAttributes[s].Comment;
			}			
			System.IO.File.WriteAllText(filename, output);
		}

		static void BuildParamSkeleton (Dictionary<string, FieldInfo> inputfields, Dictionary<string, ParameterAttribute> fieldAttributes, string filename, string executable)
		{
			string output = "@execute " + executable + "\n";
			
			foreach(string s in inputfields.Keys)
			{				
				if(fieldAttributes[s].Type == ParameterType.OutputFile)
					output += ":store_" + inputfields[s].Name + "_filename OUTFILENAME";
				else
					output += ":" + s + " " + (inputfields[s].GetCustomAttributes(typeof(ParameterAttribute), true)[0] as ParameterAttribute).DefaultValue.ToString() + "\n";
			}
			System.IO.File.WriteAllText(filename, output);
		}
		
		/// <summary>
		/// Builds a startup script, the .ct file, the .stdout file as well as a parameters skeleton
		/// </summary>
		private void BuildConfigFiles()
		{			
			Type simulationType = this.GetType();			
			
			// Collect information of input and output types
			Dictionary<string, FieldInfo> inputfields = new Dictionary<string, FieldInfo>();			
			Dictionary<string, FieldInfo> outputfields = new Dictionary<string, FieldInfo>();
			Dictionary<string,ParameterAttribute> fieldAttributes = new Dictionary<string, ParameterAttribute>();
			
			foreach(FieldInfo fi in simulationType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static))
			{
				object[] customAttributes = fi.GetCustomAttributes(typeof(ParameterAttribute), true); 
				if(customAttributes.Length>0 && ((customAttributes[0] as ParameterAttribute).Type == ParameterType.Input
					|| (customAttributes[0] as ParameterAttribute).Type == ParameterType.OutputFile))
				{
					inputfields[fi.Name] = fi;
					fieldAttributes[fi.Name] = customAttributes[0] as ParameterAttribute;
				}
				if(customAttributes.Length>0 && (customAttributes[0] as ParameterAttribute).Type == ParameterType.Output)
				{
					outputfields[fi.Name] = fi;
					fieldAttributes[fi.Name] = customAttributes[0] as ParameterAttribute;
				}
			}
			
			Logger.AddMessage(LogEntryType.AppMsg, string.Format("Extracted {0} input and {1} output parameters.", inputfields.Keys.Count, outputfields.Keys.Count));
			
			// Generate the startupscript
			BuildStartupScript (scriptname);
			Logger.AddMessage(LogEntryType.AppMsg, string.Format("Startup script \"{0}\" generated.", scriptname));
			
			// Generate .ct file from input parameters
			BuildCTFile (inputfields, fieldAttributes, scriptname + ".ct");
			Logger.AddMessage(LogEntryType.AppMsg, string.Format("pyspg script \"{0}\" generated.", scriptname + ".ct"));
						
			// Generate .stdout file from output parameters
			BuildStdOutFile (outputfields, fieldAttributes, scriptname + ".stdout");
			Logger.AddMessage(LogEntryType.AppMsg, string.Format("stdout script \"{0}\" generated.", scriptname + ".stdout"));
			
			// Generate parameters skeleton file parameters.dat
			BuildParamSkeleton (inputfields, fieldAttributes, "parameters.dat", scriptname);
			Logger.AddMessage(LogEntryType.AppMsg, string.Format("Skeleton parameter config \"{0}\" generated.", "parameters.dat"));
		}
		
		private static string TranslateType(Type t)
		{
			if(t == typeof(int))
				return "val:signed";
			else if (t == typeof(double))
				return "val:double";
			else if (t == typeof(float))
				return "val:float";
			else if (t == typeof(long))
				return "val:long";	
			else if (t.IsEnum)
				return "choice:string";
			return "?";
		}
		
		private static FieldInfo[] GetFields(Type t, Type attributeType)
		{
			List<FieldInfo> l = new List<FieldInfo>();
			foreach(FieldInfo fi in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static))
				if(fi.GetCustomAttributes(attributeType, false).Length==1)
					l.Add(fi);
			return l.ToArray();
		}
		
		public abstract void RunSimulation();
	}
}

