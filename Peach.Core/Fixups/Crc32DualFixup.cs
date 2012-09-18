﻿
//
// Copyright (c) Michael Eddington
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in	
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

// Authors:
//   Michael Eddington (mike@dejavusecurity.com)
//   Mikhail Davidov (sirus@haxsys.net)

// $Id$

using System;
using System.Collections.Generic;
using System.Text;
using Peach.Core.Dom;
using Peach.Core.Fixups.Libraries;

namespace Peach.Core.Fixups
{
	[FixupAttribute("Crc32DualFixup", "Standard CRC32 as defined by ISO 3309 applied to two elements.", true)]
	[FixupAttribute("checksums.Crc32DualFixup", "Standard CRC32 as defined by ISO 3309 applied to two elements.")]
	[ParameterAttribute("ref1", typeof(DataElement), "Reference to data element", true)]
	[ParameterAttribute("ref2", typeof(DataElement), "Reference to data element", true)]
	[Serializable]
	public class Crc32DualFixup : Fixup
	{
		bool invalidatedEvent = false;

		public Crc32DualFixup(DataElement parent, Dictionary<string, Variant> args)
			: base(parent, args)
		{
			if (!args.ContainsKey("ref1") || !args.ContainsKey("ref2"))
				throw new PeachException("Error, Crc32DualFixup requires a 'ref1' AND 'ref2' argument!");
		}

		protected override Variant fixupImpl(DataElement obj)
		{
			string objRef1 = (string)args["ref1"];
			string objRef2 = (string)args["ref2"];
			var ref1 = obj.find(objRef1);
			var ref2 = obj.find(objRef2);

			if (!invalidatedEvent)
			{
				invalidatedEvent = true;
				ref1.Invalidated += new InvalidatedEventHandler(ref1_Invalidated);
				ref2.Invalidated += new InvalidatedEventHandler(ref2_Invalidated);
			}

			if (ref1 == null)
				throw new PeachException(string.Format("Crc32DualFixup could not find ref1 element '{0}'", objRef1));
			if (ref2 == null)
				throw new PeachException(string.Format("Crc32DualFixup could not find ref2 element '{0}'", objRef2));

			byte[] data1 = ref1.Value.Value;
			byte[] data2 = ref2.Value.Value;
			byte[] data3 = new byte[data1.Length + data2.Length];
			Buffer.BlockCopy(data1, 0, data3, 0, data1.Length);
			Buffer.BlockCopy(data2, 0, data3, data1.Length, data2.Length);

			CRCTool crcTool = new CRCTool();
			crcTool.Init(CRCTool.CRCCode.CRC32);

			return new Variant((uint)crcTool.crctablefast(data3));
		}

		void ref2_Invalidated(object sender, EventArgs e)
		{
			parent.Invalidate();
		}

		void ref1_Invalidated(object sender, EventArgs e)
		{
			parent.Invalidate();
		}
	}
}

// end
