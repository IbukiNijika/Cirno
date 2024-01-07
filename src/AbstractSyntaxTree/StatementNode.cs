using Cirno.CodeGen;
using LLVMSharp.Interop;

namespace Cirno.AbstractSyntaxTree;

public abstract class StatementNode : ASTNode
{
    public override LLVMValueRef? Accept(ICodeGenVisitor visitor, LLVMBasicBlockRef? entryBasicBlock,
        LLVMBasicBlockRef? exitBasicBlock)
        => visitor.Visit(this, entryBasicBlock, entryBasicBlock);
}