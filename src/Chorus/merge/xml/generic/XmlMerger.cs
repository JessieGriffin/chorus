using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Chorus.merge.xml.generic;

namespace Chorus.merge.xml.generic
{
	public class NodeMergeResult : IMergeEventListener
	{
		private XmlNode _mergedNode;
		private IList<IConflict> _conflicts;
		//enhance: add list of changed entries to speed up import into db backends

		public NodeMergeResult()
		{
			_conflicts = new List<IConflict>();
		}

		public XmlNode MergedNode
		{
			get
			{
				return _mergedNode;
			}
			internal set { _mergedNode = value; }
		}

		public IList<IConflict> Conflicts
		{
			get { return _conflicts; }
			set { _conflicts = value; }
		}

		public void ConflictOccurred(IConflict conflict)
		{
			_conflicts.Add(conflict);
		}

		public void EnteringContext(string context)
		{

		}
	}

	public class XmlMerger
	{
		public IMergeEventListener EventListener { get; set; }
		public MergeSituation MergeSituation{ get; set;}
		public MergeStrategies MergeStrategies { get; set; }

		public XmlMerger(MergeSituation mergeSituation)
		{
			MergeSituation = mergeSituation;
			EventListener  = new NullMergeEventListener();
			MergeStrategies = new MergeStrategies();
		}


		/// <summary>
		/// use for tests
		/// </summary>
		/// <param name="ours"></param>
		/// <param name="theirs"></param>
		/// <param name="ancestor"></param>
		/// <returns></returns>
		public NodeMergeResult Merge(XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			NodeMergeResult result = new NodeMergeResult();
			DispatchingMergeEventListener dispatcher= new DispatchingMergeEventListener();
			dispatcher.AddEventListener(result);
			EventListener = dispatcher;
			MergeInner(ref ours, theirs, ancestor);
			result.MergedNode = ours;
			return result;
		}

		//review: don't know if this is going to want the result or not

		public XmlNode Merge(IMergeEventListener eventListener, XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			EventListener = eventListener;
			MergeInner(ref ours, theirs, ancestor);
			return ours;
		}

		public void MergeInner(ref XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			MergeAttributes(ref ours, theirs, ancestor);
			MergeChildren(ref ours,theirs,ancestor);
		}

		public NodeMergeResult Merge(string ourXml, string theirXml, string ancestorXml)
		{
			XmlDocument doc = new XmlDocument();
			XmlNode ourNode = XmlUtilities.GetDocumentNodeFromRawXml(ourXml, doc);
			XmlNode theirNode = XmlUtilities.GetDocumentNodeFromRawXml(theirXml, doc);
			XmlNode ancestorNode = XmlUtilities.GetDocumentNodeFromRawXml(ancestorXml, doc);

			return Merge(ourNode, theirNode, ancestorNode);
		}

		public NodeMergeResult MergeFiles(string ourPath, string theirPath, string ancestorPath)
		{
			XmlDocument ourDoc = new XmlDocument();
			ourDoc.Load(ourPath);
			XmlNode ourNode = ourDoc.DocumentElement;

			XmlDocument theirDoc = new XmlDocument();
			theirDoc.Load(theirPath);
			XmlNode theirNode = theirDoc.DocumentElement;

			XmlNode ancestorNode = null;
			if (File.Exists(ancestorPath)) // it's possible for the file to be created independently by each user, with no common ancestor
			{
				XmlDocument ancestorDoc = new XmlDocument();
				ancestorDoc.Load(ancestorPath);
				ancestorNode = ancestorDoc.DocumentElement;
			}

			return Merge(ourNode, theirNode, ancestorNode);
		}

		private static List<XmlAttribute> GetAttrs(XmlNode node)
		{
			//need to copy so we can iterate while changing
			List<XmlAttribute> attrs = new List<XmlAttribute>();
			foreach (XmlAttribute attr in node.Attributes)
			{
				attrs.Add(attr);
			}
			return attrs;
		}

		private static XmlAttribute GetAttributeOrNull(XmlNode node, string name)
		{
			if (node == null)
				return null;
			return node.Attributes.GetNamedItem(name) as XmlAttribute;
		}

		private void MergeAttributes(ref XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			foreach (XmlAttribute theirAttr in GetAttrs(theirs))
			{
				XmlAttribute ourAttr = GetAttributeOrNull(ours, theirAttr.Name);
				XmlAttribute ancestorAttr = GetAttributeOrNull(ancestor, theirAttr.Name);

				if (ourAttr == null)
				{
					if (ancestorAttr == null)
					{
						ours.Attributes.Append(theirAttr);
					}
					else if (ancestorAttr.Value == theirAttr.Value)
					{
						continue; // we deleted it, they didn't touch it
					}
					else //we deleted it, but at the same time, they changed it
					{
						//todo: should we add what they modified?
						//needs a test first

						//until then, this is a conflict
						EventListener.ConflictOccurred(new RemovedVsEditedAttributeConflict(theirAttr.Name, null, theirAttr.Value, ancestorAttr.Value, MergeSituation));
						continue;
					}
				}
				else if (ancestorAttr == null) // we both introduced this attribute
				{
					if (ourAttr.Value == theirAttr.Value)
					{
						//nothing to do
						continue;
					}
					else
					{
						EventListener.ConflictOccurred(new BothEdittedAttributeConflict(theirAttr.Name, ourAttr.Value, theirAttr.Value, null,  MergeSituation));
					}
				}
				else if (ancestorAttr.Value == ourAttr.Value)
				{
					if (ourAttr.Value == theirAttr.Value)
					{
						//nothing to do
						continue;
					}
					else //theirs is a change
					{
						ourAttr.Value = theirAttr.Value;
					}
				}
				else if (ourAttr.Value == theirAttr.Value)
				{
					//both changed to same value
					continue;
				}
				else if (ancestorAttr.Value == theirAttr.Value)
				{
					//only we changed the value
					continue;
				}
				else
				{
					EventListener.ConflictOccurred(new BothEdittedAttributeConflict(theirAttr.Name, ourAttr.Value, theirAttr.Value, ancestorAttr.Value, MergeSituation));
				}
			}

			// deal with their deletions
			foreach (XmlAttribute ourAttr in GetAttrs(ours))
			{

				XmlAttribute theirAttr = GetAttributeOrNull(theirs, ourAttr.Name);
				XmlAttribute ancestorAttr = GetAttributeOrNull(ancestor,ourAttr.Name);

				if (theirAttr == null && ancestorAttr != null)
				{
					if (ourAttr.Value == ancestorAttr.Value) //we didn't change it, they deleted it
					{
						ours.Attributes.Remove(ourAttr);
					}
					else
					{
						EventListener.ConflictOccurred(new RemovedVsEditedAttributeConflict(ourAttr.Name, ourAttr.Value, null, ancestorAttr.Value, MergeSituation));
					}
				}
			}
		}

		internal void MergeTextNodes(ref XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			if (ours.InnerText.Trim() == theirs.InnerText.Trim())
			{
				return; // we agree
			}
			if (string.IsNullOrEmpty(ours.InnerText.Trim()))
			{
				if (ancestor == null || ancestor.InnerText ==null || ancestor.InnerText.Trim()==string.Empty)
				{
					ours.InnerText = theirs.InnerText; //we had it empty
					return;
				}
				else  //we deleted it.
				{
					if (ancestor.InnerText.Trim() == theirs.InnerText.Trim())
					{
						//and they didn't touch it. So leave it deleted
						return;
					}
					else
					{
						//they edited it. Keep our removal.
						EventListener.ConflictOccurred(new RemovedVsEdittedTextConflict(ours, theirs, ancestor, MergeSituation));
						return;
					}
				}
			}
			else if ((ancestor == null) || (ours.InnerText != ancestor.InnerText))
			{
				//we're not empty, we edited it, and we don't equal theirs

				if (theirs.InnerText == null || string.IsNullOrEmpty(theirs.InnerText.Trim()))
				{
					//we edited, they deleted it. Keep ours.
					EventListener.ConflictOccurred(new RemovedVsEdittedTextConflict(ours, theirs, ancestor, MergeSituation));
					return;
				}
				else
				{
					// We know: ours is different from theirs; ours is not empty; ours is different from ancestor;
					// theirs is not empty.
					if (ancestor!=null && theirs.InnerText == ancestor.InnerText)
						return; // we edited it, they did not, keep ours.
					//both edited it. Keep ours, but report conflict.
					EventListener.ConflictOccurred(new BothEdittedTextConflict(ours, theirs, ancestor, MergeSituation));
					return;
				}
			}
			else // we didn't edit it, they did
			{
				ours.InnerText = theirs.InnerText;
			}
		}

		private void MergeChildren(ref XmlNode ours, XmlNode theirs, XmlNode ancestor)
		{
			//is this a level of the xml file that would consitute the minimal unit conflict-understanding
			//from a user perspecitve?
			//e.g., in a dictionary, this is the lexical entry.  In a text, it might be  a paragraph.
			var generator = MergeStrategies.GetElementStrategy(ours).ContextDescriptorGenerator;
			if(generator!=null)
			{
				//review: question: does this not get called at levels below the entry?
				//this would seem to fail at, say, a sense. I'm confused. (JH 30june09)

				var xpath = generator.GenerateContextDescriptor(ours.OuterXml);
				EventListener.EnteringContext(xpath);
			}

			new MergeChildrenMethod(ours, theirs, ancestor, this).Run();
		}



	}
}