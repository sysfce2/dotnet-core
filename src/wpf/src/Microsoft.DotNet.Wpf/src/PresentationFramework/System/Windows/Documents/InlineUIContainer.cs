﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// 
// Description: InlineUIContainer - a wrapper for embedded UIElements in text 
//    flow content inline collections
//

using System.Windows.Markup; // XamlDesignerSerializationManager
using MS.Internal.Documents;

namespace System.Windows.Documents
{
    /// <summary>
    /// InlineUIContainer - a wrapper for embedded UIElements in text 
    /// flow content inline collections
    /// </summary>
    [ContentProperty("Child")]
    [TextElementEditingBehaviorAttribute(IsMergeable = false)]
    public class InlineUIContainer : Inline
    {
        //-------------------------------------------------------------------
        //
        // Constructors
        //
        //-------------------------------------------------------------------

        #region Constructors

        /// <summary>
        /// Initializes a new instance of InlineUIContainer element.
        /// </summary>
        /// <remarks>
        /// The purpose of this element is to be a wrapper for UIElements
        /// when they are embedded into text flow - as items of
        /// InlineCollections.
        /// </remarks>
        public InlineUIContainer()
        {
        }

        /// <summary>
        /// Initializes an InlineBox specifying its child UIElement
        /// </summary>
        /// <param name="childUIElement">
        /// UIElement set as a child of this inline item
        /// </param>
        public InlineUIContainer(UIElement childUIElement) : this(childUIElement, null)
        {
        }

        /// <summary>
        /// Creates a new InlineUIContainer instance.
        /// </summary>
        /// <param name="childUIElement">
        /// Optional child of the new InlineUIContainer, may be null.
        /// </param>
        /// <param name="insertionPosition">
        /// Optional position at which to insert the new InlineUIContainer. May
        /// be null.
        /// </param>
        public InlineUIContainer(UIElement childUIElement, TextPointer insertionPosition)
        {
            insertionPosition?.TextContainer.BeginChange();
            try
            {
                // This will throw InvalidOperationException if schema validity is violated.
                insertionPosition?.InsertInline(this);

                this.Child = childUIElement;
            }
            finally
            {
                insertionPosition?.TextContainer.EndChange();
            }
        }

        #endregion Constructors

        //-------------------------------------------------------------------
        //
        // Public Properties
        //
        //-------------------------------------------------------------------

        #region Public Properties

        /// <summary>
        /// The content spanned by this TextElement.
        /// </summary>
        public UIElement Child
        {
            get
            {
                return this.ContentStart.GetAdjacentElement(LogicalDirection.Forward) as UIElement;
            }

            set
            {
                TextContainer textContainer = this.TextContainer;

                textContainer.BeginChange();
                try
                {
                    TextPointer contentStart = this.ContentStart;

                    UIElement child = Child;
                    if (child != null)
                    {
                        textContainer.DeleteContentInternal(contentStart, this.ContentEnd);
                        ContainerTextElementField.ClearValue(child);
                    }

                    if (value != null)
                    {
                        ContainerTextElementField.SetValue(value, this);
                        contentStart.InsertUIElement(value);
                    }
                }
                finally
                {
                    textContainer.EndChange();
                }
            }
        }

        #endregion Public Properties


        #region Internal Properties
        
        /// <summary>
        /// UIElementIsland representing embedded Element Layout island within content world.
        /// </summary>
        internal UIElementIsland UIElementIsland
        {
            get
            {
                UpdateUIElementIsland();

                return _uiElementIsland;
            }
        }

        #endregion Internal Properties


        #region Private Methods 
        
        /// <summary>
        /// Ensures the _uiElementIsland variable is up to date
        /// </summary>
        private void UpdateUIElementIsland()
        {
            UIElement childElement = this.Child;

            if(_uiElementIsland == null || _uiElementIsland.Root != childElement)
            {
                _uiElementIsland?.Dispose();
                _uiElementIsland = null;

                if(childElement != null)
                {
                    _uiElementIsland = new UIElementIsland(childElement);
                }
            }
        }

        #endregion Private Methods 


        #region Private Data

        private UIElementIsland _uiElementIsland;

        #endregion Private Data
    }
}
