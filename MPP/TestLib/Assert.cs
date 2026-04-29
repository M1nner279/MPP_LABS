using System.Collections;
using TestLib.exceptions;
using System.Linq.Expressions;

namespace TestLib;

public static class Assert
{
    public static void AreEqual<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
            throw new TestFailedException(
                $"AreEqual failed. Expected: <{expected}>. Actual: <{actual}>.");
    }
    
    public static void AreNotEqual<T>(T notExpected, T actual)
    {
        if (Equals(notExpected, actual))
            throw new TestFailedException(
                $"AreNotEqual failed. Value: <{actual}>.");
    }
    
    public static void IsTrue(bool condition)
    {
        if (!condition)
            throw new TestFailedException("IsTrue failed.");
    }
    
    public static void IsFalse(bool condition)
    {
        if (condition)
            throw new TestFailedException("IsFalse failed.");
    }
    
    public static void IsNull(object? value)
    {
        if (value != null)
            throw new TestFailedException("IsNull failed.");
    }
    
    public static void IsNotNull(object? value)
    {
        if (value == null)
            throw new TestFailedException("IsNotNull failed.");
    }
    
    public static void Contains<T>(T expected, IEnumerable<T> collection)
    {
        foreach (var item in collection)
        {
            if (Equals(item, expected))
                return;
        }

        throw new TestFailedException(
            $"Contains failed. Item <{expected}> not found.");
    }
    
    public static void DoesNotContain<T>(T notExpected, IEnumerable<T> collection)
    {
        foreach (var item in collection)
        {
            if (Equals(item, notExpected))
                throw new TestFailedException(
                    $"DoesNotContain failed. Item <{notExpected}> found.");
        }
    }
    
    public static void IsEmpty(IEnumerable collection)
    {
        foreach (var _ in collection)
            throw new TestFailedException("IsEmpty failed.");
    }
    
    public static void IsNotEmpty(IEnumerable collection)
    {
        foreach (var _ in collection)
            return;

        throw new TestFailedException("IsNotEmpty failed.");
    }
    
    public static void AreSame(object expected, object actual)
    {
        if (!ReferenceEquals(expected, actual))
            throw new TestFailedException("AreSame failed.");
    }
    
    public static void AreNotSame(object expected, object actual)
    {
        if (ReferenceEquals(expected, actual))
            throw new TestFailedException("AreNotSame failed.");
    }
    
    public static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new TestFailedException(
                $"Throws failed. Expected {typeof(TException).Name}, got {ex.GetType().Name}");
        }

        throw new TestFailedException(
            $"Throws failed. No exception thrown. Expected {typeof(TException).Name}");
    }
    
    public static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new TestFailedException(
                $"ThrowsAsync failed. Expected {typeof(TException).Name}, got {ex.GetType().Name}");
        }

        throw new TestFailedException(
            $"ThrowsAsync failed. No exception thrown. Expected {typeof(TException).Name}");
    }
    
    //expressions
    public static void That(Expression<Func<bool>> condition)
{
    // 1. Компилируем выражение в обычный делегат Func<bool> и выполняем
    var compiledCondition = condition.Compile();
    if (compiledCondition())
    {
        return; // Тест пройден
    }

    // 2. Если тест провален, разбираем структуру выражения
    string details = AnalyzeExpression(condition.Body);

    throw new TestFailedException(
        $"Assert.That failed.\n" +
        $"  Expression: {condition.Body}\n" +
        $"  Details:{details}");
}

private static string AnalyzeExpression(Expression expr)
{
    // Если это бинарное выражение (например, a == b, x > 5, y <= z)
    if (expr is BinaryExpression binary)
    {
        var leftValue = EvaluateExpression(binary.Left);
        var rightValue = EvaluateExpression(binary.Right);
        var op = GetOperatorString(binary.NodeType);

        return $"\n    Structure: Binary ({binary.NodeType})" +
               $"\n    Left:      {binary.Left} -> <{FormatValue(leftValue)}>" +
               $"\n    Operator:  {op}" +
               $"\n    Right:     {binary.Right} -> <{FormatValue(rightValue)}>";
    }
    
    // Если это вызов метода (например, text.Contains("abc"))
    if (expr is MethodCallExpression methodCall)
    {
        var instanceValue = methodCall.Object != null ? EvaluateExpression(methodCall.Object) : "static";
        var args = methodCall.Arguments.Select(EvaluateExpression).ToArray();
        var argsStr = string.Join(", ", args.Select(FormatValue));

        return $"\n    Structure: Method Call ({methodCall.Method.Name})" +
               $"\n    Instance:  {methodCall.Object?.ToString() ?? "static"} -> <{FormatValue(instanceValue)}>" +
               $"\n    Arguments: [{argsStr}]";
    }

    // Если это унарное выражение (например, !isValid)
    if (expr is UnaryExpression unary)
    {
        var operandValue = EvaluateExpression(unary.Operand);
        return $"\n    Structure: Unary ({unary.NodeType})" +
               $"\n    Operand:   {unary.Operand} -> <{FormatValue(operandValue)}>";
    }

    // Резервный вариант для прочих типов выражений
    return $"\n    Structure: {expr.NodeType}";
}

// Вспомогательный метод для вычисления значения конкретной части выражения
private static object? EvaluateExpression(Expression expr)
{
    try
    {
        // Оборачиваем выражение в конвертацию к object, создаем лямбду и выполняем
        var objectMember = Expression.Convert(expr, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        return getterLambda.Compile()();
    }
    catch
    {
        return "{Evaluation Failed}";
    }
}

private static string FormatValue(object? value)
{
    if (value is null) return "null";
    if (value is string s) return $"\"{s}\"";
    return value.ToString() ?? "null";
}

private static string GetOperatorString(ExpressionType nodeType) => nodeType switch
{
    ExpressionType.Equal => "==",
    ExpressionType.NotEqual => "!=",
    ExpressionType.GreaterThan => ">",
    ExpressionType.GreaterThanOrEqual => ">=",
    ExpressionType.LessThan => "<",
    ExpressionType.LessThanOrEqual => "<=",
    ExpressionType.AndAlso => "&&",
    ExpressionType.OrElse => "||",
    ExpressionType.Add => "+",
    ExpressionType.Subtract => "-",
    ExpressionType.Multiply => "*",
    ExpressionType.Divide => "/",
    _ => nodeType.ToString()
};
}