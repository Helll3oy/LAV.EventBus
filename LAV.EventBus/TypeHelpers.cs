using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LAV.EventBus
{
    public static class TypeHelpers
    {
        public static string GetLambdaSourceCode(this object instance, string fieldName)
        {
            var type = instance.GetType();
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new ArgumentException($"Field '{fieldName}' not found.");
            
            var lambda = field.GetValue(instance) as LambdaExpression 
                ?? throw new ArgumentException($"Field '{fieldName}' is not a lambda expression.");

            return lambda.ToString();
        }
    }
}