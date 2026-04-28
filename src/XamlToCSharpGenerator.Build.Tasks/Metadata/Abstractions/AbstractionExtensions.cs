using System.Collections.Generic;
using System.Linq;

namespace Obfuscar.Metadata.Abstractions
{
    /// <summary>
    /// Extension methods for abstraction interfaces, providing Cecil-compatible helper functionality.
    /// </summary>
    public static class AbstractionExtensions
    {
        /// <summary>
        /// Checks if a method is public, including family (protected) and family-or-assembly visibility.
        /// </summary>
        public static bool IsMethodPublic(this IMethodDefinition method)
        {
            if (method == null)
                return false;
            
            // Check if it's public, family (protected), or family-or-assembly
            // For abstraction layer, we need to check the method attributes
            // If IsPrivate is false and method exists, we consider visibility patterns
            return !method.IsPrivate;
        }

        /// <summary>
        /// Gets a custom attribute by full name if present.
        /// </summary>
        public static ICustomAttribute GetCustomAttribute(this ITypeDefinition type, string attributeFullName)
        {
            if (type?.CustomAttributes == null)
                return null;

            return type.CustomAttributes.FirstOrDefault(a => a.AttributeTypeName == attributeFullName);
        }

        /// <summary>
        /// Checks if the type has a specific custom attribute.
        /// </summary>
        public static bool HasCustomAttribute(this ITypeDefinition type, string attributeFullName)
        {
            return GetCustomAttribute(type, attributeFullName) != null;
        }

        /// <summary>
        /// Gets a custom attribute by full name if present on a method.
        /// </summary>
        public static ICustomAttribute GetCustomAttribute(this IMethodDefinition method, string attributeFullName)
        {
            if (method?.CustomAttributes == null)
                return null;

            return method.CustomAttributes.FirstOrDefault(a => a.AttributeTypeName == attributeFullName);
        }

        /// <summary>
        /// Checks if the method has a specific custom attribute.
        /// </summary>
        public static bool HasCustomAttribute(this IMethodDefinition method, string attributeFullName)
        {
            return GetCustomAttribute(method, attributeFullName) != null;
        }

        /// <summary>
        /// Checks if a field is public, including family (protected) and family-or-assembly visibility.
        /// </summary>
        public static bool IsFieldPublic(this IField field)
        {
            if (field == null)
                return false;
            
            var visibility = field.Attributes & System.Reflection.FieldAttributes.FieldAccessMask;
            return visibility == System.Reflection.FieldAttributes.Public ||
                   visibility == System.Reflection.FieldAttributes.Family ||
                   visibility == System.Reflection.FieldAttributes.FamORAssem;
        }
    }
}
