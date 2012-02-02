﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Compression;
using System.IO;
using Peach.Core.Dom;
using Peach.Core.IO;

namespace Peach.Core.Transformers.Type
{
    [TransformerAttribute("StringToFloat", "Transforms a string into an float.")]
    [TransformerAttribute("encode.StringToFloat", "Transforms a string into an float.")]
    [Serializable]
    public class StringToFloat : Transformer
    {
        public StringToFloat(Dictionary<string,Variant> args) : base(args)
		{
		}

		protected override BitStream internalEncode(BitStream data)
		{
            string dataAsStr = Encoding.ASCII.GetString(data.Value);
            float dataAsFloat = float.Parse(dataAsStr);
            return new BitStream(BitConverter.GetBytes(dataAsFloat));
		}

		protected override BitStream internalDecode(BitStream data)
		{
            throw new NotImplementedException();
		}
    }
}

// end
