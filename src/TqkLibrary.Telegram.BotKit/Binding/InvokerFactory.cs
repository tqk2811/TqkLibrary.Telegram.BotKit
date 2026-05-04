using System.Linq.Expressions;

namespace TqkLibrary.Telegram.BotKit.Binding
{
    /// <summary>
    /// Builds a compiled delegate that invokes a <see cref="MethodInfo"/> with a pre-bound
    /// argument array. Used at registry construction time so the dispatcher never pays
    /// <see cref="MethodInfo.Invoke(object?, object?[])"/> reflection cost on the hot path.
    /// </summary>
    internal static class InvokerFactory
    {
        public static Func<object, object?[], object?> Create(MethodInfo method)
        {
            ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
            ParameterExpression argsParam = Expression.Parameter(typeof(object?[]), "args");
            ParameterInfo[] parameters = method.GetParameters();

            Expression[] argExpressions = new Expression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Expression indexed = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                argExpressions[i] = Expression.Convert(indexed, parameters[i].ParameterType);
            }

            Expression target = method.IsStatic
                ? null!
                : Expression.Convert(instanceParam, method.DeclaringType!);
            Expression call = Expression.Call(target, method, argExpressions);

            Expression body;
            if (method.ReturnType == typeof(void))
                body = Expression.Block(call, Expression.Constant(null, typeof(object)));
            else if (method.ReturnType.IsValueType)
                body = Expression.Convert(call, typeof(object));
            else
                body = call;

            return Expression.Lambda<Func<object, object?[], object?>>(body, instanceParam, argsParam).Compile();
        }
    }
}
