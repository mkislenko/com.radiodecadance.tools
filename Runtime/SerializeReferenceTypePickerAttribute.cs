using System;
using UnityEngine;

namespace RadioDecadance.Tools
{
    /// <summary>
    /// Apply this attribute to a [SerializeReference] field (single or array) to enable selecting a concrete
    /// implementation type in the Inspector. For arrays, apply the attribute to the array field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class SerializeReferenceTypePickerAttribute : PropertyAttribute
    {
        /// <summary>
        /// Base type used to filter available implementations. If null, the declared field type will be used.
        /// For arrays or lists, the element type will be used.
        /// </summary>
        public Type BaseType { get; }

        /// <summary>
        /// If true, abstract types can be selected (mostly for nesting another level). Default: false.
        /// </summary>
        public bool AllowAbstract { get; set; }

        public SerializeReferenceTypePickerAttribute(Type baseType = null)
        {
            BaseType = baseType;
        }
    }
}
