![Icon](https://cloud.githubusercontent.com/assets/5763993/26522656/41e28a6e-432f-11e7-9cae-7856f927d1a1.png)

# ExpressionTranslator
Translate from linq expressions to C# code

### Get it
```
PM> Install-Package ExpressionTranslator
```

### Get readable script
You can compile expression into readable script by `ToScript` extension method
```CSharp
var script = lambda.ToScript();
```

# ExpressionDebugger
Step into debugging and generate readable script from linq expressions

### Get it
```
PM> Install-Package ExpressionDebugger
```

### Compile with debug info
`CompileWithDebugInfo` extension method will allow step-into debugging.
```CSharp
var func = lambda.CompileWithDebugInfo();
func(); //<-- you can step-into this function!!
```

### Version 2.0 .NET Core support!
- Version 2.0 now support .NET Core

### Visual Studio for Mac
To step-into debugging, you might need to emit file
```CSharp
var opt = new ExpressionCompilationOptions { EmitFile = true };
var func = lambda.CompileWithDebugInfo(opt);
func(); //<-- you can step-into this function!!
```
