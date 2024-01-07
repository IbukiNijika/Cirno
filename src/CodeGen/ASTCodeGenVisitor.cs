using System;
using System.Linq;
using Cirno.AbstractSyntaxTree;
using Cirno.DiagnosticTools;
using Cirno.Lexer;
using Cirno.SyntaxSymbol;
using LLVMSharp.Interop;

namespace Cirno.CodeGen;

public interface ICodeGenVisitable
{
    LLVMValueRef? Accept(ICodeGenVisitor visitor, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
}

public interface ICodeGenVisitor
{
    LLVMValueRef? Visit(ArraySubscriptExprNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(ASTNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(BinaryOperatorNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(CallFunctionNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(CompoundStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(DeclarationNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(ExprNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(FunctionDeclarationNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(IfStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit<TValue>(IntegerLiteral<TValue> node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(LiteralNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(ProgramNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(StatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(VariableDeclarationNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(ReturnStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
    LLVMValueRef? Visit(WhileStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock);
}

public sealed class CodeGenVisitor : ICodeGenVisitor, IDisposable
{
    private LLVMContextRef _context;
    private LLVMModuleRef _module;
    private LLVMBuilderRef _irBuilder;
    private bool _isDisposed;
    private Cirno.SyntaxSymbol.EnvSymbolTable _symbolTable;
    private Cirno.DiagnosticTools.DiagnosticList _diagnostics;
    
    public Cirno.DiagnosticTools.DiagnosticList Diagnostics => _diagnostics;
    
    public CodeGenVisitor(string moduleName, DiagnosticTools.DiagnosticList diagnostics)
    {
        _context = LLVMContextRef.Create();
        _module = _context.CreateModuleWithName("");
        _irBuilder = _context.CreateBuilder();
        
        _symbolTable = new EnvSymbolTable(null);
        _diagnostics = new DiagnosticList(diagnostics);
        PrevInitBasicEnv();
    }
    
    private void PrevInitBasicEnv()
    {
        var inputFnRetTy = _context.Int32Type;
        LLVMTypeRef[] inputFnParamsTy = [_context.VoidType];
        var inputFnTy = LLVMTypeRef.CreateFunction(inputFnRetTy, inputFnParamsTy);
        var inputFn = _module.AddFunction("input", inputFnTy);
        
        _symbolTable.Add("input",
            new FunctionSymbol(new SyntaxToken(SyntaxKind.FunctionExpression, "input", 0, 0), "input", inputFn, inputFnTy));
        
        var outputFnRetTy = _context.VoidType;
        LLVMTypeRef[] outputFnParamsTy = [_context.Int32Type];
        var outputFnTy = LLVMTypeRef.CreateFunction(outputFnRetTy, outputFnParamsTy);
        var outputFn = _module.AddFunction("output", outputFnTy);
        
        _symbolTable.Add("input",
            new FunctionSymbol(new SyntaxToken(SyntaxKind.FunctionExpression, "output", 0, 0), "output", outputFn, outputFnTy));

    }
    
    ~CodeGenVisitor()
    {
        Dispose(false);
    }
    
    public void Dispose()
    {
        Dispose(true);
        System.GC.SuppressFinalize(this);
    }
    
    private void Dispose(bool disposing)
    {
        if (_isDisposed) 
            return;
        
        if (disposing)
        {
            
        }
        
        _irBuilder.Dispose();
        _module.Dispose();
        _context.Dispose();

        _isDisposed = true;
    }

    /// <summary>
    /// 数组下标访问
    /// </summary>
    /// <param name="node"></param>
    /// <param name="entryBasicBlock"></param>
    /// <param name="exitBasicBlock"></param>
    /// <returns>i32*</returns>
    public LLVMValueRef? Visit(ArraySubscriptExprNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        if (!_symbolTable.TryGetSymbolFromLinkTable(node.Name, out var arrayValue))
        {
            _diagnostics.ReportSemanticError(new TextLocation(node.Position.Line, node.Position.Col), "");
            return null;
        }

        if (arrayValue is null)
        {
            _diagnostics.ReportSemanticError(new TextLocation(node.Position.Line, node.Position.Col), "");
        }
        
        if (arrayValue?.Value.TypeOf.ElementType.Kind is not LLVMTypeKind.LLVMArrayTypeKind)
        {
            _diagnostics.ReportNotExpectType(new TextLocation(node.Position.Line, node.Position.Col),
                arrayValue?.Name ?? "", ValueTypeKind.IntArray, arrayValue?.TypeKind ?? ValueTypeKind.Void);
        }

        var maybeIndex = node.OffsetExpr.Accept(this, entryBasicBlock, exitBasicBlock);
        if (maybeIndex is null)
        {
            _diagnostics.ReportSemanticError(new TextLocation(node.Position.Line, node.Position.Col),
                $"Invalid offset for {arrayValue?.Name ?? ""}");
            return null;
        }

        var arr = arrayValue!.Value;
        
        var index = maybeIndex.Value;
        if (index.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && index.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            var idx = _irBuilder.BuildLoad2(index.TypeOf.ElementType, index);
            var item = _irBuilder.BuildGEP2(arr.TypeOf.ElementType, arr, [LLVMValueRef.CreateConstInt(_context.Int32Type, 0), idx]);
            return item;
        }
        else if (index.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            var item = _irBuilder.BuildGEP2(arr.TypeOf.ElementType, arr, [LLVMValueRef.CreateConstInt(_context.Int32Type, 0), index]);
            return item;
        }
        
        _diagnostics.ReportSemanticError(new TextLocation(node.Position.Line, node.Position.Col), 
            $"Array index must be an integer.");
        return null;
    }

    public LLVMValueRef? Visit(ASTNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        // throw new NotImplementedException();
        foreach (var nextNode in node.GetChildren())
        {
            nextNode.Accept(this, entryBasicBlock, exitBasicBlock);
        }

        return null;
    }

    public LLVMValueRef? Visit(BinaryOperatorNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        var leftResult = node.Left.Accept(this, entryBasicBlock, exitBasicBlock);
        var rightResult = node.Right!.Accept(this, entryBasicBlock, exitBasicBlock);

        if (leftResult is null || rightResult is null)
        {
            return null;
        }

        var left = leftResult.Value;
        var right = rightResult.Value;

        return node.OpKind switch
        {
            BinaryOperatorKind.Addition => BuildAdd(left, right, node),
            BinaryOperatorKind.Subtraction => BuildSub(left, right, node),
            BinaryOperatorKind.Multiplication => BuildMul(left, right, node),
            BinaryOperatorKind.Division => BuildDiv(left, right, node),
            BinaryOperatorKind.Assignment => BuildAssign(left, right, node),
            BinaryOperatorKind.EqualTo => BuildCmp(left, right, LLVMIntPredicate.LLVMIntEQ, node),
            BinaryOperatorKind.NotEqualTo => BuildCmp(left, right, LLVMIntPredicate.LLVMIntNE, node),
            BinaryOperatorKind.LessThan => BuildCmp(left, right, LLVMIntPredicate.LLVMIntSLT, node),
            BinaryOperatorKind.LessThanOrEqualTo => BuildCmp(left, right, LLVMIntPredicate.LLVMIntSLE, node),
            BinaryOperatorKind.GreaterThanOrEqualTo => BuildCmp(left, right, LLVMIntPredicate.LLVMIntSGE, node),
            BinaryOperatorKind.GreaterThan => BuildCmp(left, right, LLVMIntPredicate.LLVMIntSGT, node),
            BinaryOperatorKind.Unknown => null,
            null => null,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private LLVMValueRef? BuildAssign(LLVMValueRef left, LLVMValueRef right, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind)
        {
            if (right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
            {
                _irBuilder.BuildStore(right, left);
                return left;
            }

            if (right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
            {
                _irBuilder.BuildStore(_irBuilder.BuildLoad2(right.TypeOf.ElementType, right), left);
                return left;
            }

            _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
            return null;
        }
        else
        {
            _diagnostics.ReportNotLeftValueError(new TextLocation(node.Position.Line, node.Position.Col), node.Left.Name);
            return null;
        } 
    }
    
    private LLVMValueRef? BuildAdd(LLVMValueRef left, LLVMValueRef right, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
            right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWAdd(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWAdd(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                right
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                   right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWAdd(
                left,
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWAdd(left, right);
        }

        _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
        return null;
    }

    private LLVMValueRef? BuildSub(LLVMValueRef left, LLVMValueRef right, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
            right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWSub(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWSub(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                right
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                   right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWSub(
                left,
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWSub(left, right);
        }

        _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
        return null;
    }
    
    private LLVMValueRef? BuildMul(LLVMValueRef left, LLVMValueRef right, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
            right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWMul(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWMul(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                right
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                   right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWMul(
                left,
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildNSWMul(left, right);
        }

        _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
        return null;
    }
    
    private LLVMValueRef? BuildDiv(LLVMValueRef left, LLVMValueRef right, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
            right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildSDiv(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildSDiv(
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                right
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                   right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildSDiv(
                left,
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildSDiv(left, right);
        }

        _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
        return null;
    }

    private LLVMValueRef? BuildCmp(LLVMValueRef left, LLVMValueRef right, LLVMIntPredicate opt, BinaryOperatorNode node)
    {
        if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
            right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildICmp(
                opt,
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind && left.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildICmp(
                opt,
                _irBuilder.BuildLoad2(left.TypeOf.ElementType, left),
                right
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind &&
                   right.TypeOf.ElementType.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildICmp(
                opt,
                left,
                _irBuilder.BuildLoad2(right.TypeOf.ElementType, right)
            );
        } else if (left.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind &&
                   right.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind)
        {
            return _irBuilder.BuildICmp(opt, left, right);
        }

        _diagnostics.ReportUnknownValueTypeError(new TextLocation(node.Position.Line, node.Position.Line));
        return null;
    }

    public LLVMValueRef? Visit(CallFunctionNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        if (!_symbolTable.TryGetSymbolFromLinkTable(node.Name, out var symbol) && symbol is null)
        {
            _diagnostics.ReportUndefinedVariableError(new TextLocation(node.Position.Line, node.Position.Col), node.Name);
            return null;
        }

        var func = symbol!.Value;
        if (func.TypeOf.ElementType.Kind is not LLVMTypeKind.LLVMFunctionTypeKind)
        {
            return null;
        }

        var argsArr = node.Args.Select(expr => expr.Accept(this, entryBasicBlock, exitBasicBlock)).ToArray();
        if (argsArr.Any(item => item?.TypeOf.Kind is LLVMTypeKind.LLVMIntegerTypeKind || item?.TypeOf is
                { Kind: LLVMTypeKind.LLVMPointerTypeKind, ElementType.Kind: LLVMTypeKind.LLVMIntegerTypeKind }))
        {
            _diagnostics.ReportCallFunctionArgsNotFullError(new TextLocation(node.Position.Line, node.Position.Col), node.Name, node.Args.Length);
            return null;
        }

        var args = argsArr.Select(item => item!.Value).ToArray();
        return _irBuilder.BuildCall2(func.TypeOf.ElementType, func, args);
    }

    public LLVMValueRef? Visit(CompoundStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(DeclarationNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(ExprNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(FunctionDeclarationNode node, LLVMBasicBlockRef? entryBasicBlock,
        LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(IfStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit<TValue>(IntegerLiteral<TValue> node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        if (node.Value is int v1)
        {
            return LLVMValueRef.CreateConstInt(_context.Int32Type, Convert.ToUInt64(long.Abs(v1)), v1 < 0);
        }

        throw new Exception($"Error Integer Type {node.Value?.GetType().Name}");
    }

    public LLVMValueRef? Visit(LiteralNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(ProgramNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        if (node.DeclarationNodes.LastOrDefault(item => item.NodeType is ASTNodeType.FunctionDeclaration)
                ?.Name is not "main")
        {
            _diagnostics.ReportSemanticError(TextLocation.NoPosition, "Last function must be main.");
        }

        foreach (var exprNode in node.DeclarationNodes)
        {
            exprNode.Accept(this, entryBasicBlock, exitBasicBlock);
        }

        return null;
    }

    public LLVMValueRef? Visit(StatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(VariableDeclarationNode node, LLVMBasicBlockRef? entryBasicBlock,
        LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }

    public LLVMValueRef? Visit(ReturnStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        if (entryBasicBlock is null)
        {
            throw new ArgumentNullException(nameof(entryBasicBlock));
        }
        
        _irBuilder.PositionAtEnd(entryBasicBlock.Value);
        var result = node.ReturnExpr?.Accept(this, entryBasicBlock, exitBasicBlock);
        if (result is null)
        {
            return _irBuilder.BuildRetVoid();
        }

        if (result.Value.TypeOf.Kind is LLVMTypeKind.LLVMPointerTypeKind)
        {
            var basicType = result.Value.TypeOf.ElementType;
            return _irBuilder.BuildRet(
                    _irBuilder.BuildLoad2(basicType, result.Value)
                );
        }
        else
        {
            return _irBuilder.BuildRet(result.Value);
        }
    }

    public LLVMValueRef? Visit(WhileStatementNode node, LLVMBasicBlockRef? entryBasicBlock, LLVMBasicBlockRef? exitBasicBlock)
    {
        throw new NotImplementedException();
    }
}