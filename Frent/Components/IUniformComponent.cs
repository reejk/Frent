﻿using Frent.Variadic.Generator;
using static Frent.AttributeHelpers;

namespace Frent.Components;

public interface IUniformComponent<TUniform> : IComponentBase
{
    void Update(TUniform uniform);
}

[Variadic(TArgFrom, TArgPattern, 15)]
[Variadic(RefArgFrom, RefArgPattern)]
public interface IUniformComponent<TUniform, TArg> : IComponentBase
{
    void Update(TUniform uniform, ref TArg arg);
}