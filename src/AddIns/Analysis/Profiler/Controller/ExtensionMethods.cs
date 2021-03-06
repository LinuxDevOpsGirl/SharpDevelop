﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using ICSharpCode.Profiler.Controller.Data;

namespace ICSharpCode.Profiler.Controller
{
	/// <summary>
	/// Extension Methods for the Profiler.
	/// </summary>
	public static class Extensions
	{
		/// <summary>
		/// Returns a string containing all errors.
		/// </summary>
		public static string GetErrorString(this CompilerErrorCollection collection)
		{
			if (collection == null)
				throw new ArgumentNullException("collection");

			StringBuilder builder = new StringBuilder();

			foreach (CompilerError err in collection)
				builder.AppendLine(err.ToString());

			return builder.ToString();
		}
		
		/// <summary>
		/// Returns a CallTreeNode with all merged items.
		/// </summary>
		public static CallTreeNode Merge(this IQueryable<CallTreeNode> items)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			
			return items.Provider.Execute<CallTreeNode>(
				Expression.Call(Data.Linq.KnownMembers.Queryable_Merge, items.Expression));
		}
		
		/// <summary>
		/// Returns a CallTreeNode with all merged items.
		/// </summary>
		public static CallTreeNode Merge(this IEnumerable<CallTreeNode> items)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			
			var itemList = items.ToList();
			return itemList.First().Merge(items);
		}
		
		/// <summary>
		/// Merges CallTreeNodes with identical name mappings.
		/// </summary>
		public static IQueryable<CallTreeNode> MergeByName(this IQueryable<CallTreeNode> items)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			
			return items.Provider.CreateQuery<CallTreeNode>(
				Expression.Call(Data.Linq.KnownMembers.Queryable_MergeByName, items.Expression));
		}
		
		/// <summary>
		/// Merges CallTreeNodes with identical name mappings.
		/// </summary>
		public static IEnumerable<CallTreeNode> MergeByName(this IEnumerable<CallTreeNode> items)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			
			return items.GroupBy(c => c.NameMapping).Select(g => g.Merge());
		}
		
		/// <summary>
		/// Tells the query execution to add logging to the query.
		/// </summary>
		public static IQueryable<CallTreeNode> WithQueryLog(this IQueryable<CallTreeNode> items, TextWriter logOutput)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			if (logOutput == null)
				throw new ArgumentNullException("logOutput");
			
			return items.Provider.CreateQuery<CallTreeNode>(
				Expression.Call(Data.Linq.KnownMembers.Queryable_WithQueryLog, items.Expression, Expression.Constant(logOutput)));
		}
		
		/// <summary>
		/// Tells the query execution to add logging to the query.
		/// </summary>
		public static IEnumerable<CallTreeNode> WithQueryLog(this IEnumerable<CallTreeNode> items, TextWriter logOutput)
		{
			if (items == null)
				throw new ArgumentNullException("items");
			if (logOutput == null)
				throw new ArgumentNullException("logOutput");
			
			IQueryable<CallTreeNode> query = items as IQueryable<CallTreeNode>;
			if (query != null)
				return query.WithQueryLog(logOutput);
			
			logOutput.WriteLine("The query did not use LINQ-to-Profiler.");
			return items;
		}
		
		/// <summary>
		/// Creates a comma separated string. The string is encoded so that it can be split
		/// into the original parts even if the inputs contains commas.
		/// </summary>
		public static string CreateSeparatedString(this IEnumerable<string> inputs)
		{
			return CreateSeparatedString(inputs, ',');
		}
		
		/// <summary>
		/// Creates a separated string using the specified separator. The string is encoded
		/// so that it can be split into the original parts even if the inputs contain the separator.
		/// </summary>
		public static string CreateSeparatedString(this IEnumerable<string> inputs, char separator)
		{
			if (inputs == null)
				throw new ArgumentNullException("inputs");
			if (separator == '"')
				throw new ArgumentException("Invalid separator");
			
			StringBuilder b = new StringBuilder();
			bool first = true;
			foreach (string input in inputs) {
				if (input == null)
					throw new ArgumentNullException("inputs", "An element in inputs is null");
				
				if (first)
					first = false;
				else
					b.Append(separator);
				
				if (input.Length > 0 && input.IndexOf(separator) < 0 && input.IndexOf('"') < 0) {
					b.Append(input);
				} else {
					b.Append('"');
					foreach (char c in input) {
						if (c == '"')
							b.Append("\"\"");
						else
							b.Append(c);
					}
					b.Append('"');
				}
			}
			return b.ToString();
		}
		
		/// <summary>
		/// Splits a comma-separated string.
		/// </summary>
		public static IList<string> SplitSeparatedString(this string input)
		{
			return SplitSeparatedString(input, ',');
		}
		
		/// <summary>
		/// Splits a separated string using the specified separator.
		/// </summary>
		public static IList<string> SplitSeparatedString(this string input, char separator)
		{
			if (input == null)
				throw new ArgumentNullException("input");
			if (separator == '"')
				throw new ArgumentException("Invalid separator");
			
			List<string> result = new List<string>();
			for (int i = 0; i < input.Length; i++) {
				Debug.Assert(i == 0 || input[i - 1] == separator);
				if (input[i] == '"') {
					i++;
					StringBuilder b = new StringBuilder();
					for (; i < input.Length; i++) {
						char c = input[i];
						if (c == '"') {
							i++;
							if (i < input.Length && input[i] == '"')
								b.Append('"');
							else
								break;
						} else {
							b.Append(c);
						}
					}
					result.Add(b.ToString());
					// i is now positioned before separator
				} else {
					int end = input.IndexOf(separator, i);
					if (end < 0) end = input.Length;
					result.Add(input.Substring(i, end - i));
					i = end; // position i before separator
				}
			}
			return result;
		}
	}
}
