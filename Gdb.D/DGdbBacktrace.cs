// DGdbBacktrace.cs
//
// Author:
//   Ludovit Lucenic <llucenic@gmail.com>,
//   Alexander Bothe
//
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// This permission notice shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Collections.Generic;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Debugger.Gdb;
using D_Parser.Misc.Mangling;


namespace MonoDevelop.Debugger.Gdb.D
{
	/// <summary>
	/// High-level component that handles gathering variables and their individual values.
	/// </summary>
	class DGdbBacktrace : GdbBacktrace
	{
		#region Properties
		List<string>[] VariableNameCache;
		List<string>[] ParameterNameCache;
		public int CurrentFrameIndex;
		public readonly VariableValueExamination Variables;

		public DGdbSession DSession {
			get { return session as DGdbSession; }
		}
		#endregion

		#region Constructor/Init
		public DGdbBacktrace (GdbSession session, long threadId, int count, ResultData firstFrame)
			: base(session, threadId, count, firstFrame)
		{
			Variables = new VariableValueExamination (this);
			DebuggingService.CurrentFrameChanged += FrameChanged;
		}

		#endregion

		#region Stack frames
		public override StackFrame[] GetStackFrames (int firstIndex, int lastIndex)
		{
			var frames =  base.GetStackFrames (firstIndex, lastIndex);
			VariableNameCache = new List<string>[frames.Length];
			ParameterNameCache = new List<string>[frames.Length];
			return frames;
		}

		void FrameChanged(Object o, EventArgs ea)
		{
			Variables.NeedsResolutionContextUpdate = true;
			CurrentFrameIndex = DebuggingService.CurrentFrameIndex;
		}

		protected override StackFrame CreateFrame(ResultData frameData)
		{
			string lang = "D";
			string func = frameData.GetValueString("func");
			string sadr = frameData.GetValueString("addr");

			int line;
			int.TryParse(frameData.GetValueString("line"),out line);

			string sfile = frameData.GetValueString("fullname");
			if (sfile == null)
				sfile = frameData.GetValueString("file");
			if (sfile == null)
				sfile = frameData.GetValueString("from");

			// demangle D function/method name stored in func
			var typeDecl = Demangler.DemangleQualifier(func);
			if (typeDecl != null)
				func = typeDecl.ToString();

			long addr = 0;
			if (!string.IsNullOrEmpty(sadr))
				addr = long.Parse(sadr.Substring(2), System.Globalization.NumberStyles.HexNumber);

			return new StackFrame(addr, new SourceLocation(func ?? "<undefined>", sfile, line), lang);
		}
		#endregion

		#region Variables
		bool isCallingCreateVarObjectImplicitly = false;
		public override ObjectValue[] GetParameters (int frameIndex, EvaluationOptions options)
		{
			isCallingCreateVarObjectImplicitly = true;
			if(CurrentFrameIndex != frameIndex)
				Variables.NeedsResolutionContextUpdate = true;
			CurrentFrameIndex = frameIndex;

			var r = base.GetParameters (frameIndex, options);

			if(ParameterNameCache[frameIndex] == null)
			{
				var nameCache = new List<string>();
				foreach(var p in r)
					nameCache.Add(p.Name);
				ParameterNameCache[frameIndex] = nameCache;
			}

			isCallingCreateVarObjectImplicitly = false;
			return r;
		}

		public override ObjectValue[] GetLocalVariables (int frameIndex, EvaluationOptions options)
		{
			isCallingCreateVarObjectImplicitly = true;
			var r = base.GetLocalVariables (frameIndex, options);

			if(VariableNameCache[frameIndex] == null)
			{
				var nameCache = new List<string>();
				foreach(var p in r)
					nameCache.Add(p.Name);
				VariableNameCache[frameIndex] = nameCache;
			}

			isCallingCreateVarObjectImplicitly = false;
			return r;
		}

		protected override ObjectValue CreateVarObject(string exp)
		{
			session.SelectThread(threadId);

			if (DebuggingService.CurrentFrameIndex != CurrentFrameIndex) {
				CurrentFrameIndex = DebuggingService.CurrentFrameIndex;
				Variables.NeedsResolutionContextUpdate = true;
			}

			if (!isCallingCreateVarObjectImplicitly) {
				var nameCache = ParameterNameCache [CurrentFrameIndex];
				if (nameCache != null && !nameCache.Contains (exp) &&
				    (VariableNameCache[CurrentFrameIndex] == null ||
					!VariableNameCache [CurrentFrameIndex].Contains (exp)))
					return ObjectValue.CreateUnknown(exp);
			}

			return Variables.EvaluateVariable (exp);
		}

		/// <summary>
		/// Used when viewing variable contents in the dedicated window in MonoDevelop.
		/// </summary>
		public override object GetRawValue (ObjectPath path, EvaluationOptions options)
		{
			return null;
			// GdbCommandResult res = DSession.RunCommand("-var-evaluate-expression", path.ToString());
				
			//return new RawValueString(new DGdbRawValueString("N/A"));
		}
		#endregion
	}

	class DGdbDissassemblyBuffer : GdbDissassemblyBuffer
	{
		public DGdbDissassemblyBuffer(DGdbSession session, long addr) : base (session, addr)
		{
		}
	}

	class DGdbRawValueString : IRawValueString
	{
		String rawString;

		public DGdbRawValueString(String rawString)
		{
			this.rawString = rawString;
		}

		public string Substring(int index, int length)
		{
			return this.rawString.Substring(index, length);
		}

		public string Value
		{
			get {
				return this.rawString;
			}
		}

		public int Length
		{
			get {
				return this.rawString.Length;
			}
		}

	}
}