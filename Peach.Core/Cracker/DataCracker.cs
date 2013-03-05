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

// $Id$

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Peach.Core.Dom;
using Peach.Core.IO;

using NLog;

namespace Peach.Core.Cracker
{
	#region Event Delegates

	public delegate void EnterHandleNodeEventHandler(DataElement element, long position);
	public delegate void ExitHandleNodeEventHandler(DataElement element, long position);
	public delegate void ExceptionHandleNodeEventHandler(DataElement element, long position, Exception e);
	public delegate void PlacementEventHandler(DataElement oldElement, DataElement newElement, DataElementContainer oldParent);

	#endregion

	/// <summary>
	/// Crack data into a DataModel.
	/// </summary>
	public class DataCracker
	{
		#region Private Members

		static NLog.Logger logger = LogManager.GetCurrentClassLogger();

		#region Position Class

		/// <summary>
		/// Helper class for tracking positions of cracked elements
		/// </summary>
		class Position
		{
			public long begin;
			public long end;
			public long? size;

			public override string ToString()
			{
				return "Begin: {0}, Size: {1}, End: {2}".Fmt(
					begin,
					size.HasValue ? size.Value.ToString() : "<null>",
					end);
			}
		}

		#endregion

		/// <summary>
		/// Collection of all elements that have been cracked so far.
		/// </summary>
		OrderedDictionary<DataElement, Position> _sizedElements;

		/// <summary>
		/// List of all unresolved size relations.
		/// This occurs when the 'Of' is cracked before the 'From'.
		/// </summary>
		List<SizeRelation> _sizeRelations;

		/// <summary>
		/// Stack of all BitStream objects passed to CrackData().
		/// This is used for determining absolute locations from relative offsets.
		/// </summary>
		List<BitStream> _dataStack = new List<BitStream>();

		/// <summary>
		/// Elements that have analyzers attached.  We run them all post-crack.
		/// </summary>
		List<DataElement> _elementsWithAnalyzer = new List<DataElement>();

		#endregion

		#region Events

		public event EnterHandleNodeEventHandler EnterHandleNodeEvent;
		protected void OnEnterHandleNodeEvent(DataElement element, long position)
		{
			if(EnterHandleNodeEvent != null)
				EnterHandleNodeEvent(element, position);
		}
		
		public event ExitHandleNodeEventHandler ExitHandleNodeEvent;
		protected void OnExitHandleNodeEvent(DataElement element, long position)
		{
			if (ExitHandleNodeEvent != null)
				ExitHandleNodeEvent(element, position);
		}

		public event ExceptionHandleNodeEventHandler ExceptionHandleNodeEvent;
		protected void OnExceptionHandleNodeEvent(DataElement element, long position, Exception e)
		{
			if (ExceptionHandleNodeEvent != null)
				ExceptionHandleNodeEvent(element, position, e);
		}

		public event PlacementEventHandler PlacementEvent;
		protected void OnPlacementEvent(DataElement oldElement, DataElement newElement, DataElementContainer oldParent)
		{
			if (PlacementEvent != null)
				PlacementEvent(oldElement, newElement, oldParent);
		}

		#endregion

		#region Public Methods

		/// <summary>
		/// Main entry method that will take a data stream and parse it into a data model.
		/// </summary>
		/// <remarks>
		/// Method will throw one of two exceptions on an error: CrackingFailure, or NotEnoughDataException.
		/// </remarks>
		/// <param name="model">DataModel to import data into</param>
		/// <param name="data">Data stream to read data from</param>
		public void CrackData(DataElement element, BitStream data)
		{
			try
			{
				_dataStack.Insert(0, data);

				if (_dataStack.Count == 1)
					handleRoot(element, data);
				else
					handleNode(element, data);
			}
			finally
			{
				_dataStack.RemoveAt(0);
			}

		}

		/// <summary>
		/// Get the size of an element that has already been cracked.
		/// The size only has a value if the element has a length attribute
		/// or the element has a size relation that has successfully resolved.
		/// </summary>
		/// <param name="elem">Element to query</param>
		/// <returns>size of the element</returns>
		public long? GetElementSize(DataElement elem)
		{
			return _sizedElements[elem].size;
		}

		/// <summary>
		/// Perform optimizations of data model for cracking
		/// </summary>
		/// <remarks>
		/// Optimization can be performed once on a data model and used
		/// for any clones made.  Optimizations will increase the speed
		/// of data cracking.
		/// </remarks>
		/// <param name="model">DataModel to optimize</param>
		public void OptimizeDataModel(DataModel model)
		{
			foreach (var element in model.EnumerateElementsUpTree())
			{
				if (element is Choice)
				{
					// TODO - Fast CACHE IT!
				}
			}
		}

		#endregion

		#region Private Helpers

		long getDataOffset()
		{
			var curr = _dataStack.First();
			var root = _dataStack.Last();

			if (curr == root)
				return 0;

			long offset = root.TellBits() - curr.LengthBits;
			System.Diagnostics.Debug.Assert(offset >= 0);
			return offset;
		}

		#endregion

		#region Handlers

		#region Top Level Handlers

		void handleRoot(DataElement element, BitStream data)
		{
			_sizedElements = new OrderedDictionary<DataElement, Position>();
			_sizeRelations = new List<SizeRelation>();

			// Crack the model
			handleNode(element, data);

			// Handle any Placement's
			handlePlacement(element, data);

			// Handle any analyzers
			foreach (DataElement elem in _elementsWithAnalyzer)
				elem.analyzer.asDataElement(elem, null);
		}

		/// <summary>
		/// Called to crack a DataElement based on an input stream.  This method
		/// will hand cracking off to a more specific method after performing
		/// some common tasks.
		/// </summary>
		/// <param name="element">DataElement to crack</param>
		/// <param name="data">Input stream to use for data</param>
		void handleNode(DataElement elem, BitStream data)
		{
			try
			{
				if (elem == null)
					throw new ArgumentNullException("elem");
				if (data == null)
					throw new ArgumentNullException("data");

				logger.Debug("------------------------------------");
				logger.Debug("{0} {1}", elem.debugName, data.Progress);

				var pos = handleNodeBegin(elem, data);

				if (elem.transformer != null)
				{
					var sizedData = elem.ReadSizedData(data, pos.size);
					var decodedData = elem.transformer.decode(sizedData);

					// Use the size of the transformed data as the new size of the element
					handleCrack(elem, decodedData, decodedData.LengthBits);
				}
				else
				{
					handleCrack(elem, data, pos.size);
				}

				if (elem.constraint != null)
					handleConstraint(elem, data);

				if (elem.analyzer != null)
					_elementsWithAnalyzer.Add(elem);

				handleNodeEnd(elem, data, pos);
			}
			catch (Exception e)
			{
				handleException(elem, data, e);
				throw;
			}
		}

		void handlePlacement(DataElement model, BitStream data)
		{
			List<DataElement> elementsWithPlacement = new List<DataElement>();
			foreach (DataElement element in model.EnumerateAllElements())
			{
				if (element.placement != null)
					elementsWithPlacement.Add(element);
			}

			foreach (DataElement element in elementsWithPlacement)
			{
				var fixups = new List<Tuple<Fixup, string>>();
				DataElementContainer oldParent = element.parent;

				// Ensure relations are resolved
				foreach (Relation relation in element.relations)
				{
					if (relation.Of != element && relation.From != element)
						throw new CrackingFailure("Error, unable to resolve Relations of/from to match current element.", element, data);
				}

				// Locate relevant fixups
				foreach (DataElement child in model.EnumerateAllElements())
				{
					if (child.fixup == null)
						continue;

					foreach (var item in child.fixup.references)
					{
						if (item.Item2 != element.name)
							continue;

						var refElem = child.find(item.Item2);
						if (refElem == null)
							throw new CrackingFailure("Error, unable to resolve Fixup reference to match current element.", element, data);

						if (refElem == element)
							fixups.Add(new Tuple<Fixup, string>(child.fixup, item.Item1));
					}
				}

				DataElement newElem = null;

				if (element.placement.after != null)
				{
					var after = element.find(element.placement.after);
					if (after == null)
						throw new CrackingFailure("Error, unable to resolve Placement on element '" + element.fullName +
							"' with 'after' == '" + element.placement.after + "'.", element, data);
					newElem = element.MoveAfter(after);
				}
				else if (element.placement.before != null)
				{
					DataElement before = element.find(element.placement.before);
					if (before == null)
						throw new CrackingFailure("Error, unable to resolve Placement on element '" + element.fullName +
							"' with 'after' == '" + element.placement.after + "'.", element, data);
					newElem = element.MoveBefore(before);
				}

				// Update fixups
				foreach (var fixup in fixups)
				{
					fixup.Item1.updateRef(fixup.Item2, newElem.fullName);
				}

				OnPlacementEvent(element, newElem, oldParent);
			}
		}

		#endregion

		#region Helpers

		void handleOffsetRelation(DataElement element, BitStream data)
		{
			long? offset = getRelativeOffset(element, data, 0);

			if (!offset.HasValue)
				return;

			offset += data.TellBits();

			if (offset > data.LengthBits)
				data.WantBytes((offset.Value + 7 - data.LengthBits) / 8);

			if (offset > data.LengthBits)
			{
				string msg = "{0} has offset of {1} bits but buffer only has {2} bits.".Fmt(
					element.debugName, offset, data.LengthBits);
				throw new CrackingFailure(msg, element, data);
			}

			data.SeekBits(offset.Value, System.IO.SeekOrigin.Begin);
		}

		void handleException(DataElement elem, BitStream data, Exception e)
		{
			_sizedElements.Remove(elem);
			_sizeRelations.RemoveAll(r => r.Of == elem);

			CrackingFailure ex = e as CrackingFailure;
			if (ex != null)
			{
				logger.Debug("{0} failed to crack.", elem.debugName);
				if (!ex.logged)
					logger.Debug(ex.Message);
				ex.logged = true;
			}
			else
			{
				logger.Debug("Exception occured: {0}", e.ToString());
			}

			OnExceptionHandleNodeEvent(elem, data.TellBits(), e);
		}

		void handleConstraint(DataElement element, BitStream data)
		{
			logger.Debug("Running constraint [" + element.constraint + "]");

			Dictionary<string, object> scope = new Dictionary<string, object>();
			scope["element"] = element;

			var iv = element.InternalValue;
			if (iv.GetVariantType() == Variant.VariantType.ByteString || iv.GetVariantType() == Variant.VariantType.BitStream)
			{
				scope["value"] = (byte[])iv;
				logger.Debug("Constraint, value=byte array.");
			}
			else
			{
				scope["value"] = (string)iv;
				logger.Debug("Constraint, value=[" + (string)iv + "].");
			}

			object oReturn = Scripting.EvalExpression(element.constraint, scope);

			if (!((bool)oReturn))
				throw new CrackingFailure("Constraint failed.", element, data);
		}

		Position handleNodeBegin(DataElement elem, BitStream data)
		{
			handleOffsetRelation(elem, data);

			System.Diagnostics.Debug.Assert(!_sizedElements.ContainsKey(elem));

			long? size = getSize(elem, data);

			var pos = new Position();
			pos.begin = data.TellBits() + getDataOffset();
			pos.size = size;

			_sizedElements.Add(elem, pos);

			// If this element does not have a size but has a size relation,
			// keep track of the relation for evaluation in the future
			if (!size.HasValue)
			{
				SizeRelation rel = elem.relations.getOfSizeRelation();
				if (rel != null)
					_sizeRelations.Add(rel);
			}

			OnEnterHandleNodeEvent(elem, pos.begin);

			return pos;
		}

		void handleNodeEnd(DataElement elem, BitStream data, Position pos)
		{
			// Completing this element might allow us to evaluate
			// outstanding size reation computations.
			for (int i = _sizeRelations.Count - 1; i >= 0; --i)
			{
				var rel = _sizeRelations[i];

				if (elem == rel.From || (elem is DataElementContainer &&
					((DataElementContainer)elem).isParentOf(rel.From)))
				{
					var other = _sizedElements[rel.Of];
					System.Diagnostics.Debug.Assert(!other.size.HasValue);
					other.size = rel.GetValue();
					_sizeRelations.RemoveAt(i);

					logger.Debug("Size relation of {0} cracked. Updating size: {1}",
						rel.Of.debugName, other.size);
				}
			}

			// Mark the end position of this element
			pos.end = data.TellBits() + getDataOffset();

			OnExitHandleNodeEvent(elem, pos.end);
		}

		void handleCrack(DataElement elem, BitStream data, long? size)
		{
			logger.Debug("Crack: {0} Size: {1}, {2}", elem.debugName,
				size.HasValue ? size.ToString() : "<null>", data.Progress);

			elem.Crack(this, data, size);
		}

		#endregion

		#endregion

		#region Calculate Element Size

		long? getRelativeOffset(DataElement elem, BitStream data, long minOffset = 0)
		{
			OffsetRelation rel = elem.relations.getOfOffsetRelation();

			if (rel == null)
				return null;

			// Ensure we have cracked the from half of the relation
			if (!_sizedElements.ContainsKey(rel.From))
				return null;

			// Offset is in bytes
			long offset = (long)rel.GetValue() * 8;

			if (rel.isRelativeOffset)
			{
				DataElement from = rel.From;

				if (rel.relativeTo != null)
					from = from.find(rel.relativeTo);

				if (from == null)
					throw new CrackingFailure("Unable to locate 'relativeTo' element in relation attached to " +
						elem.debugName + "'.", elem, data);

				// Get the position we are related to
				Position pos;
				if (!_sizedElements.TryGetValue(from, out pos))
					return null;

				// If relativeTo, offset is from beginning of relativeTo element
				// Otherwise, offset is after the From element
				offset += rel.relativeTo != null ? pos.begin : pos.end;
			}

			// Adjust offset to be relative to the current BitStream
			offset -= getDataOffset();

			// Ensure the offset is not before our current position
			if (offset < data.TellBits())
			{
				string msg = "{0} has offset of {1} bits but already read {2} bits.".Fmt(
					elem.debugName, offset, data.TellBits());
				throw new CrackingFailure(msg, elem, data);
			}

			// Make offset relative to current position
			offset -= data.TellBits();

			// Ensure the offset satisfies the minimum
			if (offset < minOffset)
			{
				string msg = "{0} has offset of {1} bits but must be at least {2} bits.".Fmt(
					elem.debugName, offset, minOffset);
				throw new CrackingFailure(msg, elem, data);
			}

			return offset;
		}

		/// <summary>
		/// Searches data for the first occurance of token starting at offset.
		/// </summary>
		/// <param name="data">BitStream to search in.</param>
		/// <param name="token">BitStream to search for.</param>
		/// <param name="offset">How many bits after the current position of data to start searching.</param>
		/// <returns>The location of the token in data from the current position or null.</returns>
		long? findToken(BitStream data, BitStream token, long offset)
		{
			while (true)
			{
				long start = data.TellBits();
				long end = data.IndexOf(token, start + offset);

				if (end >= 0)
					return end - start - offset;

				long dataLen = data.LengthBytes;
				data.WantBytes(token.LengthBytes);

				if (dataLen == data.LengthBytes)
					return null;
			}
		}

		bool? scanArray(Dom.Array array, ref long pos, List<Mark> tokens, Until until)
		{
			logger.Debug("scanArray: {0}", array.debugName);

			int tokenCount = tokens.Count;
			long arrayPos = 0;
			var ret = scan(array.origionalElement, ref arrayPos, tokens, null, until);

			for (int i = tokenCount; i < tokens.Count; ++i)
			{
				tokens[i].Optional = array.minOccurs == 0;
				tokens[i].Position += pos;
			}

			if (!ret.HasValue || !ret.HasValue)
			{
				logger.Debug("scanArray: {0} -> {1}", array.debugName,
					ret.HasValue ? "Deterministic" : "Unsized");
				return ret;
			}

			if (until == Until.FirstSized)
				ret = false;

			var rel = array.relations.getOfCountRelation();
			if (rel != null && _sizedElements.ContainsKey(rel.From))
			{
				arrayPos *= rel.GetValue();
				pos += arrayPos;
				logger.Debug("scanArray: {0} -> Count Relation: {1}, Size: {2}",
					array.debugName, rel.GetValue(), arrayPos);
				return ret;
			}
			else if (array.minOccurs == 1 && array.maxOccurs == 1)
			{
				arrayPos *= array.occurs;
				pos += arrayPos;
				logger.Debug("scanArray: {0} -> Occurs: {1}, Size: {2}",
					array.debugName, array.occurs, arrayPos);
				return ret;
			}

			for (int i = tokenCount; i < tokens.Count; ++i)
			{
				long? where = findToken(_dataStack.First(), tokens[i].Element.Value, tokens[i].Position);
				if (!where.HasValue && tokens[i].Optional)
				{
					logger.Debug("scanArray: {0} -> Missing Token, minOccurs==0", array.debugName);
					return true;
				}
			}

			logger.Debug("scanArray: {0} -> Count Unknown", array.debugName);
			return null;
		}

		class Mark
		{
			public DataElement Element { get; set; }
			public long Position { get; set; }
			public bool Optional { get; set; }
		}

		enum Until { FirstSized, FirstUnsized };

		/// <summary>
		/// Scan elem and all children looking for a target element.
		/// The target can either be the first sized element or the first unsized element.
		/// If an unsized element is found, keep track of the determinism of the element.
		/// An element is determinstic if its size is unknown, but can be determined by calling
		/// crack(). Examples are a container with sized children or a null terminated string.
		/// </summary>
		/// <param name="elem">Element to start scanning at.</param>
		/// <param name="pos">The position of the scanner when 'until' occurs.</param>
		/// <param name="tokens">List of tokens found when scanning.</param>
		/// <param name="end">If non-null and an element with an offset relation is detected,
		/// record the element's absolute position and stop scanning.</param>
		/// <param name="until">When to stop scanning.
		/// Either first sized element or first unsized element.</param>
		/// <returns>Null if an unsized element was found.
		/// False if a deterministic element was found.
		/// True if all elements are sized.</returns>
		bool? scan(DataElement elem, ref long pos, List<Mark> tokens, Mark end, Until until)
		{
			if (elem.isToken)
			{
				tokens.Add(new Mark() { Element = elem, Position = pos, Optional = false });
				logger.Debug("scan: {0} -> Pos: {1}, Saving Token", elem.debugName, pos);
			}

			if (end != null)
			{
				long? offRel = getRelativeOffset(elem, _dataStack.First(), pos);
				if (offRel.HasValue)
				{
					end.Element = elem;
					end.Position = offRel.Value;
					logger.Debug("scan: {0} -> Pos: {1}, Offset relation: {2}", elem.debugName, pos, end.Position);
					return true;
				}
			}

			// See if we have a size relation
			SizeRelation sizeRel = elem.relations.getOfSizeRelation();
			if (sizeRel != null)
			{
				if (_sizedElements.ContainsKey(sizeRel.From))
				{
					pos += sizeRel.GetValue();
					logger.Debug("scan: {0} -> Pos: {1}, Size relation: {2}", elem.debugName, pos, sizeRel.GetValue());
					return true;
				}
				else
				{
					logger.Debug("scan: {0} -> Pos: {1}, Size relation: ???", elem.debugName, pos);
					return false;
				}
			}

			// See if our length is defined
			if (elem.hasLength)
			{
				pos += elem.lengthAsBits;
				logger.Debug("scan: {0} -> Pos: {1}, Length: {2}", elem.debugName, pos, elem.lengthAsBits);
				return true;
			}

			// See if our length is determinstic, size is determined by cracking
			if (elem.isDeterministic)
			{
				logger.Debug("scan: {0} -> Pos: {1}, Determinstic", elem.debugName, pos);
				return false;
			}

			// If we are unsized, see if we are a container
			var cont = elem as DataElementContainer;
			if (cont == null)
			{
				logger.Debug("scan: {0} -> Offset: {1}, Unsized element", elem.debugName, pos);
				return null;
			}

			// Elements with transformers require a size
			if (cont.transformer != null)
			{
				logger.Debug("scan: {0} -> Offset: {1}, Unsized transformer", elem.debugName, pos);
				return null;
			}

			// Treat choices as unsized
			if (cont is Dom.Choice)
			{
				logger.Debug("scan: {0} -> Offset: {1}, Unsized choice", elem.debugName, pos);
				return null;
			}

			if (cont is Dom.Array)
			{
				return scanArray((Dom.Array)cont, ref pos, tokens, until);
			}

			logger.Debug("scan: {0}", elem.debugName);

			foreach (var child in cont)
			{
				bool? ret = scan(child, ref pos, tokens, end, until);

				// An unsized element was found
				if (!ret.HasValue)
					return ret;

				// Aa unsized but deterministic element was found
				if (ret.Value == false)
					return ret;

				// If we are looking for the first sized element than this
				// element size is determined by cracking all the children
				if (until == Until.FirstSized)
					return false;
			}

			// All children are sized, so we are sized
			return true;
		}

		/// <summary>
		/// Get the size of the data element.
		/// </summary>
		/// <param name="elem">Element to size</param>
		/// <param name="data">Bits to crack</param>
		/// <returns>Null if size is unknown or the size in bits.</returns>
		long? getSize(DataElement elem, BitStream data)
		{
			logger.Debug("getSize: -----> {0}", elem.debugName);

			long pos = 0;

			var ret = scan(elem, ref pos, new List<Mark>(), null, Until.FirstSized);

			if (ret.HasValue)
			{
				if (ret.Value)
				{
					logger.Debug("getSize: <----- Size: {0}", pos);
					return pos;
				}

				logger.Debug("getSize: <----- Deterministic: ???");
				return null;
			}

			var tokens = new List<Mark>();
			var end = new Mark();

			ret = lookahead(elem, ref pos, tokens, end);

			// 1st priority, end placement
			if (end.Element != null)
			{
				pos = end.Position - pos;
				logger.Debug("getSize: <----- Placement: {0}", pos);
				return pos;
			}

			// 2nd priority, last unsized element
			if (ret.HasValue)
			{
				if (ret.Value && (pos != 0 || !(elem is DataElementContainer)))
				{
					pos = data.LengthBits - (data.TellBits() + pos);
					logger.Debug("getSize: <----- Last Unsized: {0}", pos);
					return pos;
				}

				logger.Debug("getSize: <----- Last Unsized: ???");
				return null;
			}

			// 3rd priority, token scan
			foreach (var token in tokens)
			{
				long? where = findToken(data, token.Element.Value, token.Position);
				if (where.HasValue || !token.Optional)
				{
					logger.Debug("getSize: <----- {0}{1} Token: {2}",
						where.HasValue ? "" : "Missing ",
						token.Optional ? "Optional" : "Required",
						where.HasValue ? where.ToString() : "???");
					return where;
				}
			}

			if (tokens.Count > 0)
			{
				pos = data.LengthBits - (data.TellBits() + pos);
				logger.Debug("getSize: <----- Missing Optional Token: {0}", pos);
				return pos;
			}

			logger.Debug("getSize: <----- Not Last Unsized: ???");
			return null;
		}

		/// <summary>
		/// Scan all elements after elem looking for the first unsized element.
		/// If an unsized element is found, keep track of the determinism of the element.
		/// An element is determinstic if its size is unknown, but can be determined by calling
		/// crack(). Examples are a container with sized children or a null terminated string.
		/// </summary>
		/// <param name="elem">Start scanning at this element's next sibling.</param>
		/// <param name="pos">The position of the scanner when 'until' occurs.</param>
		/// <param name="tokens">List of tokens found when scanning.</param>
		/// <param name="end">If non-null and an element with an offset relation is detected,
		/// record the element's absolute position and stop scanning.</param>
		/// Either first sized element or first unsized element.</param>
		/// <returns>Null if an unsized element was found.
		/// False if a deterministic element was found.
		/// True if all elements are sized.</returns>
		bool? lookahead(DataElement elem, ref long pos, List<Mark> tokens, Mark end)
		{
			logger.Debug("lookahead: {0}", elem.debugName);

			// Ensure all elements are sized until we reach either
			// 1) A token
			// 2) An offset relation we have cracked that can be satisfied
			// 3) The end of the data model

			DataElement prev = elem;

			while (true)
			{
				// Get the next sibling
				var curr = prev.nextSibling();

				if (curr != null)
				{
					var ret = scan(curr, ref pos, tokens, end, Until.FirstUnsized);
					if (!ret.HasValue || ret.Value == false)
						return ret;

					if (end.Element != null)
						return true;
				}
				else if (prev.parent == null)
				{
					// hit the top
					break;
				}
				else if (GetElementSize(prev.parent).HasValue)
				{
					// Parent is bound by size
					break;
				}
				else
				{
					if (!(elem is DataElementContainer) && (prev.parent is Dom.Array))
					{
						long arrayPos = pos;
						var ret = scanArray((Dom.Array)prev.parent, ref arrayPos, tokens, Until.FirstUnsized);
						if (!ret.HasValue || ret.Value == false)
							return ret;
					}

					// no more siblings, ascend
					curr = prev.parent;
				}

				prev = curr;
			}

			return true;
		}

		#endregion
	}
}

// end
