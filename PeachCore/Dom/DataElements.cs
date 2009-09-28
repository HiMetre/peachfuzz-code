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
//   Michael Eddington (mike@phed.org)

// $Id$

using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Reflection;

namespace PeachCore.Dom
{
	public enum LengthType
	{
		String,
		Python,
		Calc
	}

	/// <summary>
	/// Base class for all data element relations
	/// </summary>
	public abstract class Relation
	{
		protected string _ofName = null;
		protected string _fromName = null;
		protected DataElement _of = null;
		protected DataElement _from = null;
		protected string _expressionGet = null;
		protected string _expressionSet = null;

		public DataElement Of
		{
			get { return _of; }
			set
			{
				if (_of != null)
				{
					// Remove existing event
					_of.Invalidated -= new InvalidatedEventHandler(OfInvalidated);
				}

				_of = value;
				_of.Invalidated += new InvalidatedEventHandler(OfInvalidated);
			}
		}

		public DataElement From
		{
			get { return _from; }
			set { _from = value; }
		}

		/// <summary>
		/// Handle invalidated event from "of" side of
		/// relation.  Need to invalidate "from".
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		public void OfInvalidated(object sender, EventArgs e)
		{
			// Invalidate 'from' side
			_from.Invalidate();
		}

		/// <summary>
		/// Get value from relation as int.
		/// </summary>
		public abstract Variant GetValue();

		/// <summary>
		/// Set value on from side
		/// </summary>
		/// <param name="value"></param>
		public abstract void SetValue(Variant value);
	}

	public class SizeRelation : Relation
	{
		public override Variant GetValue()
		{
			int size = _of.GetValue().Length;

			// TODO: Call expressionGet

			return new Variant(size);
		}

		public override void SetValue(Variant value)
		{
			throw new NotImplementedException();
		}
	}

	public class CountRelation : Relation
	{
		public override Variant GetValue()
		{
			throw new NotImplementedException();
		}

		public override void SetValue(Variant value)
		{
			throw new NotImplementedException();
		}
	}

	public class OffsetRelation : Relation
	{
		public override Variant GetValue()
		{
			throw new NotImplementedException();
		}

		public override void SetValue(Variant value)
		{
			throw new NotImplementedException();
		}
	}

	/// <summary>
	/// Transformers perform static transforms to 
	/// data generated by DataElements.
	/// 
	/// Transformers can be chaned.
	/// </summary>
	public abstract class Transformer
	{
		Transformer _nextTransformer = null;

		public Transformer NextTransformer
		{
			get { return _nextTransformer; }
			set { _nextTransformer = value; }
		}

		public byte[] encode(byte[] data)
		{
			byte[] ret = _encode(data);

			if (_nextTransformer != null)
				return _nextTransformer.encode(ret);

			return ret;
		}

		public byte[] decode(byte[] data)
		{
			byte[] ret;

			if (_nextTransformer != null)
			{
				ret = _nextTransformer.decode(data);
				return _decode(ret);
			}
			   
			return _decode(data);
		}

		public abstract byte[] _encode(byte[] data);
		public abstract byte[] _decode(byte[] data);
	}

	public delegate void InvalidatedEventHandler(object sender, EventArgs e);
	public delegate void DefaultValueChangedEventHandler(object sender, EventArgs e);
	public delegate void MutatedValueChangedEventHandler(object sender, EventArgs e);

	/// <summary>
	/// Base class for all data elements.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public abstract class DataElement
	{
		/// <summary>
		/// Mutated vale override's fixup
		///
		///  - Default Value
		///  - Relation
		///  - Fixup
		///  - Type contraints
		///  - Transformer
		/// </summary>
		public const uint MUTATE_OVERRIDE_FIXUP = 0x1;
		/// <summary>
		/// Mutated value overrides transformers
		/// </summary>
		public const uint MUTATE_OVERRIDE_TRANSFORMER = 0x2;
		/// <summary>
		/// Mutated value overrides type constraints (e.g. string length,
		/// null terminated, etc.)
		/// </summary>
		public const uint MUTATE_OVERRIDE_TYPE_CONSTRAINTS = 0x4;
		/// <summary>
		/// Mutated value overrides relations.
		/// </summary>
		public const uint MUTATE_OVERRIDE_RELATIONS = 0x8;
		/// <summary>
		/// Default mutate value
		/// </summary>
		public const uint MUTATE_DEFAULT = MUTATE_OVERRIDE_FIXUP;

		public string name;
		public bool isMutable = true;
		public uint mutationFlags = MUTATE_DEFAULT;

		protected Variant _defaultValue;
		protected Variant _mutatedValue;

		protected List<Relation> _relations = new List<Relation>();
		protected Fixup _fixup = null;
		protected Transformer _transformer = null;

		protected DataElement _parent;

		protected Variant _internalValue;
		protected byte[] _value;

		#region Events

		public event InvalidatedEventHandler Invalidated;
		public event DefaultValueChangedEventHandler DefaultValueChanged;
		public event MutatedValueChangedEventHandler MutatedValueChanged;

		protected virtual void OnInvalidated(EventArgs e)
		{
			_internalValue = GetInternalValue();
			_value = GetValue();

            // Bubble this up the chain
            if(_parent)
                _parent.Invalidate();

			if (Invalidated != null)
				Invalidated(this, e);
		}

		protected virtual void OnDefaultValueChanged(EventArgs e)
		{
			if (DefaultValueChanged != null)
				DefaultValueChanged(this, e);
		}

		protected virtual void OnMutatedValueChanged(EventArgs e)
		{
			OnInvalidated(null);

			if (MutatedValueChanged != null)
				MutatedValueChanged(this, e);
		}

		#endregion

		public static OrderedDictionary<string, Type> dataElements = new OrderedDictionary<string, Type>();
		public static void loadDataElements(Assembly assembly)
		{
			foreach (Type type in assembly.GetTypes())
			{
				if (type.IsClass && !type.IsAbstract)
				{
					object [] attr = type.GetCustomAttributes(typeof(DataElementAttribute), false);
					DataElementAttribute dea = attr[0] as DataElementAttribute;
					if (!dataElements.ContainsKey(dea.elementName))
					{
						dataElements.Add(type);
					}
				}
			}
		}

		static DataElement()
		{
		}

		/// <summary>
		/// Call to invalidate current element and cause rebuilding
		/// of data elements dependent on this element.
		/// </summary>
		public void Invalidate()
		{
		}

		public virtual bool isLeafNode
		{
			get { return true; }
		}

		/// <summary>
		/// Default value for this data element.
		/// </summary>
		public virtual Variant DefaultValue
		{
			get { return _defaultValue; }
			set
			{
				_defaultValue = value;
				OnDefaultValueChanged(null);
			}
		}

		/// <summary>
		/// Current mutated value (if any) for this data element.
		/// </summary>
		public virtual Variant MutatedValue
		{
			get { return _mutatedValue; }
			set
			{
				_mutatedValue = value;
				OnMutatedValueChanged(null);
			}
		}

        /// <summary>
        /// Get the Internal Value of this data element
        /// </summary>
		public virtual Variant InternalValue
		{
			get { return _internalValue; }
		}

        /// <summary>
        /// Get the final Value of this data element
        /// </summary>
		public virtual byte[] Value
		{
			get { return _value; }
		}

		/// <summary>
		/// Generate the internal value of this data element
		/// </summary>
		/// <returns>Internal value in .NET form</returns>
		public virtual Variant GenerateInternalValue()
		{
			Variant value;

			// 1. Default value

			value = DefaultValue;

			// 2. Relations

			if (_mutatedValue != null && (mutationFlags & MUTATE_OVERRIDE_RELATIONS) != 0)
				return MutatedValue;

			foreach(Relation r in _relations)
			{
				if (r.Of == this)
				{
					value = r.GetValue();
				}
			}

			// 3. Fixup

			if (_mutatedValue != null && (mutationFlags & MUTATE_OVERRIDE_FIXUP) != 0)
				return MutatedValue;

			if (_fixup != null)
				value = _fixup.fixup(this);

			return value;
		}

		protected virtual byte[] InternalValueToByteArray(Variant b)
		{
			return (byte[])b;
		}

		/// <summary>
		/// Generate the final value of this data element
		/// </summary>
		/// <returns></returns>
		public byte[] GenerateValue()
		{
			if (_mutatedValue != null && (mutationFlags & MUTATE_OVERRIDE_TRANSFORMER) != 0)
				return (byte[]) MutatedValue;

			byte[] value = InternalValueToByteArray(_internalValue);

			if(_transformer != null)
				return _transformer.encode(value);

			return value;
		}

		/// <summary>
		/// Find data element with specific name.
		/// </summary>
		/// <param name="name">Name to search for</param>
		/// <returns>Returns found data element or null.</returns>
		public DataElement find(string name)
		{
			throw new ApplicationException("TODO");
		}
	}

	/// <summary>
	/// Abstract base class for DataElements that contain other
	/// data elements.  Such as Block, Choice, or Flags.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public abstract class DataElementContainer : DataElement, IEnumerable<DataElement>, IList<DataElement>
	{
		protected List<DataElement> _childrenList;
		protected Dictionary<string, DataElement> _childrenDict;

		public override bool isLeafNode
		{
			get { return false; }
		}

		public DataElement this[int index]
		{
			get { return _childrenList[index]; }
			set { throw new NotImplementedException(); }
		}

		public DataElement this[string key]
		{
			get { return _childrenDict[key]; }
			set { throw new NotImplementedException(); }
		}

		#region IEnumerable<Element> Members

		public IEnumerator<DataElement> GetEnumerator()
		{
			return _childrenList.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _childrenList.GetEnumerator();
		}

		#endregion

		#region IList<DataElement> Members

		public int IndexOf(DataElement item)
		{
			return _childrenList.IndexOf(item);
		}

		public void Insert(int index, DataElement item)
		{
			_childrenList.Insert(index, item);
		}

		public void RemoveAt(int index)
		{
			_childrenList.RemoveAt(index);
		}

		#endregion

		#region ICollection<DataElement> Members

		public void Add(DataElement item)
		{
			_childrenList.Add(item);
		}

		public void Clear()
		{
			_childrenList.Clear();
		}

		public bool Contains(DataElement item)
		{
			_childrenList.Contains(item);
		}

		public void CopyTo(DataElement[] array, int arrayIndex)
		{
			_childrenList.CopyTo(array, arrayIndex);
		}

		public int Count
		{
			get { return _childrenList.Count; }
		}

		public bool IsReadOnly
		{
			get { return _childrenList.IsReadOnly; }
		}

		public bool Remove(DataElement item)
		{
			return _childrenList.Remove(item);
		}

		#endregion
	}

	/// <summary>
	/// Block element
	/// </summary>
	[DataElement("Block")]
	[DataElementChildSupportedAttribute(DataElementTypes.Any)]
	//[ParameterAttribute("length", typeof(uint), "Length of string in characters", false)]
	public class Block : DataElementContainer
	{
		/// <summary>
		/// Get the internal value of this data element.
		/// </summary>
		/// <returns>Internal value in .NET form</returns>
		public override Variant InternalValue
		{
			get
			{
				List<byte> value = new List<byte>();

				// 1. Mutated value if any

				if (_mutatedValue != null
					&& ((mutationFlags & MUTATE_OVERRIDE_FIXUP) == 0
					|| (mutationFlags & MUTATE_OVERRIDE_RELATIONS) == 0))
				{
					return _mutatedValue;
				}

				// 2. Default value

				foreach (DataElement child in this)
					value.AddRange(child.GetValue());

				return new Variant(value.ToArray());
			}
		}
	}

	/// <summary>
	/// DataModel is just a top level Block.
	/// </summary>
	public class DataModel : Block
	{
	}

	/// <summary>
	/// Choice allows the selection of a single
	/// data element based on the current data set.
	/// 
	/// The other options in the choice are available
	/// for mutation by the mutators.
	/// </summary>
	[DataElement("Choice")]
	[DataElementChildSupportedAttribute(DataElementTypes.Any)]
	//[ParameterAttribute("length", typeof(uint), "Length of string in characters", false)]
	public class Choice : DataElementContainer
	{
	}

	/// <summary>
	/// Array of data elements.  Can be
	/// zero or more elements.
	/// </summary>
	public class Array : DataElementContainer
	{
		public uint minOccurs = 1;
		public uint maxOccurs = 1;

		/// <summary>
		/// Origional data element to use as 
		/// template for other data elements in 
		/// array.
		/// </summary>
		public DataElement origionalElemement = null;
	}

	/// <summary>
	/// A numerical data element.
	/// </summary>
	[DataElement("Number")]
	[DataElementChildSupportedAttribute(DataElementTypes.NonDataElements)]
	[ParameterAttribute("size", typeof(uint), "Size in bits [8, 16, 24, 32, 64]", true)]
	[ParameterAttribute("signed", typeof(bool), "Is number signed (default false)", false)]
	[ParameterAttribute("endian", typeof(string), "Byte order of number (default 'little')", false)]
	public class Number : DataElement
	{
		protected int _size = 8;
		protected bool _signed = true;
		protected bool _isLittleEndian = true;

		protected override byte[] InternalValueToByteArray(Variant b)
		{
			byte[] value = null;

			if (_signed)
			{
				switch (_size)
				{
					case 8:
						value = BitConverter.GetBytes((sbyte)(int)b);
						break;
					case 16:
						value = BitConverter.GetBytes((short)(int)b);
						break;
					case 32:
						value = BitConverter.GetBytes((Int32)(int)b);
						break;
					case 64:
						value = BitConverter.GetBytes((Int64)(int)b);
						break;
				}
			}
			else
			{
				switch (_size)
				{
					case 8:
						value = BitConverter.GetBytes((byte)(int)b);
						break;
					case 16:
						value = BitConverter.GetBytes((ushort)(int)b);
						break;
					case 32:
						value = BitConverter.GetBytes((UInt32)(int)b);
						break;
					case 64:
						value = BitConverter.GetBytes((UInt64)(int)b);
						break;
				}
			}

			// Handle endian in ghetto manner
			if (BitConverter.IsLittleEndian != _isLittleEndian)
				Array.Reverse(value);

			return value;
		}
	}

	public enum StringType
	{
		Ascii,
		Utf7,
		Utf8,
		Utf16,
		Utf16be,
		Utf32
	}

	/// <summary>
	/// String data element
	/// </summary>
	[DataElement("String")]
	[DataElementChildSupportedAttribute(DataElementTypes.NonDataElements)]
	//[ParameterAttribute("size", typeof(uint), "Size in bits [8, 16, 24, 32, 64]", true)]
	public class String : DataElement
	{
		protected StringType _type = StringType.Ascii;
		protected bool _nullTerminated;
		protected uint _length;
		protected string _lengthOther;
		protected LengthType _lengthType;

		protected override byte[] InternalValueToByteArray(Variant v)
		{
			byte[] value = null;

			if (_type == StringType.Ascii)
				value = Encoding.ASCII.GetBytes((string)v);

			else if (_type == StringType.Utf7)
				value = Encoding.UTF7.GetBytes((string)v);

			else if (_type == StringType.Utf8)
				value = Encoding.UTF8.GetBytes((string)v);

			else if (_type == StringType.Utf16)
				value = Encoding.Unicode.GetBytes((string)v);

			else if (_type == StringType.Utf16be)
				value = Encoding.BigEndianUnicode.GetBytes((string)v);

			else if (_type == StringType.Utf32)
				value = Encoding.UTF32.GetBytes((string)v);

			else
				throw new ApplicationException("String._type not set properly!");
			
			return value;
		}
	}

	/// <summary>
	/// Binary large object data element
	/// </summary>
	[DataElement("Blob")]
	[DataElementChildSupportedAttribute(DataElementTypes.NonDataElements)]
	//[ParameterAttribute("size", typeof(uint), "Size in bits [8, 16, 24, 32, 64]", true)]
	public class Blob : DataElement
	{
		protected uint _length;

	}

	[DataElement("Flags")]
	[DataElementChildSupportedAttribute(DataElementTypes.NonDataElements)]
	[DataElementChildSupportedAttribute("Flag")]
	//[ParameterAttribute("size", typeof(uint), "Size in bits [8, 16, 24, 32, 64]", true)]
	public class Flags : DataElement
	{
	}

	[DataElement("Flag")]
	[DataElementChildSupportedAttribute(DataElementTypes.NonDataElements)]
	//[ParameterAttribute("size", typeof(uint), "Size in bits [8, 16, 24, 32, 64]", true)]
	public class Flag : DataElement
	{
	}

	/// <summary>
	/// Variant class emulates untyped scripting languages
	/// variables were typing can change as needed.  This class
	/// solves the problem of boxing internal types.  Instead
	/// explicit casts are used to access the value as needed.
	/// 
	/// TODO: Investigate implicit casting as well.
	/// TODO: Investigate deligates for type -> byte[] conversion.
	/// </summary>
	public class Variant
	{
		protected enum VariantType
		{
			Unknown,
			Int,
			String,
			ByteString
		}

		VariantType _type = VariantType.Unknown;
		int _valueInt;
		string _valueString;
		byte[] _valueByteArray;

		public Variant(int v)
		{
			SetValue(v);
		}

		public Variant(string v)
		{
			SetValue(v);
		}
		
		public Variant(byte[] v)
		{
			SetValue(v);
		}

		public void SetValue(int v)
		{
			_type = VariantType.Int;
			_valueInt = v;
		}

		public void SetValue(string v)
		{
			_type = VariantType.String;
			_valueString = v;
		}

		public void SetValue(byte[] v)
		{
			_type = VariantType.ByteString;
			_valueByteArray = v;
		}

		/// <summary>
		/// Access variant as an int value.
		/// </summary>
		/// <param name="v">Variant to cast</param>
		/// <returns>int representation of value</returns>
		public static explicit operator int(Variant v)
		{
			switch (v._type)
			{
				case VariantType.Int:
					return v._valueInt;
				case VariantType.String:
					return Convert.ToInt32(v._valueString);
				case VariantType.ByteString:
					throw new NotSupportedException("Unable to convert byte[] to int type.");
				default:
					throw new NotSupportedException("Unable to convert to unknown type.");
			}
		}

		/// <summary>
		/// Access variant as string value.
		/// </summary>
		/// <param name="v">Variant to cast</param>
		/// <returns>string representation of value</returns>
		public static explicit operator string(Variant v)
		{
			switch (v._type)
			{
				case VariantType.Int:
					return Convert.ToString(v._valueInt);
				case VariantType.String:
					return v._valueString;
				case VariantType.ByteString:
					throw new NotSupportedException("Unable to convert byte[] to string type.");
				default:
					throw new NotSupportedException("Unable to convert to unknown type.");
			}
		}

		/// <summary>
		/// Access variant as byte[] value.  This type is currently limited
		/// as neather int or string's are properly cast to byte[] since 
		/// additional information is needed.
		/// 
		/// TODO: Investigate using deligates to handle conversion.
		/// </summary>
		/// <param name="v">Variant to cast</param>
		/// <returns>byte[] representation of value</returns>
		public static explicit operator byte[](Variant v)
		{
			switch (v._type)
			{
				case VariantType.Int:
					throw new NotSupportedException("Unable to convert int to byte[] type.");
				case VariantType.String:
					throw new NotSupportedException("Unable to convert string to byte[] type.");
				case VariantType.ByteString:
					return v._valueByteArray;
				default:
					throw new NotSupportedException("Unable to convert to unknown type.");
			}
		}
	}

}

// end
