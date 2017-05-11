![Icon](http://www.fancyicons.com/free-icons/112/must-have/png/128/pause_128.png)

# ExpressionDebugger
Step into debugging and generate readable script from linq expressions

### Get it
```
PM> Install-Package Mapster
```

### Get readable script
You can compile expression into readable script by `ToScript` extension method
```CSharp
var script = lambda.ToScript();
```

### Compile with debug info
`CompileWithDebugInfo` extension method will allow step-into debugging.
```CSharp
var func = lambda.CompileWithDebugInfo();
func(); //<-- you can step-into this function!!
```

In order to step-into debugging, make sure you turn off just my code feature.
![turn off just my code](https://cloud.githubusercontent.com/assets/5763993/23740682/47608676-04d7-11e7-842d-77c18a459515.png)

#### Net Core support
Currently, step-info debugging is working only in .NET 4.x. .NET Core doesn't support this feature yet.
