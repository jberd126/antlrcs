/*
 * [The "BSD license"]
 * Copyright (c) 2011 Terence Parr
 * All rights reserved.
 *
 * Conversion to C#:
 * Copyright (c) 2011 Sam Harwell, Tunnel Vision Laboratories, LLC
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * 3. The name of the author may not be used to endorse or promote products
 *    derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 * IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT,
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 * DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 * THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
 * THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

namespace Antlr4.StringTemplate.Compiler
{
    using System.Collections.Generic;
    using System.Linq;
    using Antlr.Runtime;
    using Antlr.Runtime.Tree;
    using Antlr4.StringTemplate.Misc;
    using ArgumentNullException = System.ArgumentNullException;
    using Console = System.Console;
    using StringWriter = System.IO.StringWriter;

    /** The result of compiling an Template.  Contains all the bytecode instructions,
     *  string table, bytecode address to source code map, and other bookkeeping
     *  info.  It's the implementation of an Template you might say.  All instances
     *  of the same template share a single implementation (impl field).
     */
    public class CompiledTemplate
    {
        private string _name;

        /** The original, immutable pattern (not really used again after
         *  initial "compilation"). Useful for debugging.  Even for
         *  subtemplates, this is entire overall template.
         */
        private string _template;

        /** The token that begins template definition; could be &lt;@r&gt; of region. */
        private IToken _templateDefStartToken;

        /** Overall token stream for template (debug only) */
        private ITokenStream _tokens;

        /** How do we interpret syntax of template? (debug only) */
        private CommonTree _ast;

        private List<FormalArgument> _formalArguments;

        private bool _hasFormalArgs;

        /** A list of all regions and subtemplates */
        private List<CompiledTemplate> implicitlyDefinedTemplates;

        private int _numberOfArgsWithDefaultValues;

        /** The group that physically defines this Template definition.  We use it to initiate
         *  interpretation via Template.ToString().  From there, it becomes field 'group'
         *  in interpreter and is fixed until rendering completes.
         */
        private TemplateGroup _nativeGroup = TemplateGroup.defaultGroup;

        /** Does this template come from a &lt;@region&gt;...&lt;@end&gt; embedded in
         *  another template?
         */
        private bool isRegion;

        /** If someone refs &lt;@r()&gt; in template t, an implicit
         *
         *   @t.r() ::= ""
         *
         *  is defined, but you can overwrite this def by defining your
         *  own.  We need to prevent more than one manual def though.  Between
         *  this var and isEmbeddedRegion we can determine these cases.
         */
        private Template.RegionType regionDefType;

        private bool isAnonSubtemplate; // {...}

        public string[] strings;     // string operands of instructions
        public byte[] instrs;        // byte-addressable code memory.
        public int codeSize;
        public Interval[] sourceMap; // maps IP to range in template pattern

        public CompiledTemplate()
        {
            instrs = new byte[TemplateCompiler.InitialCodeSize];
            sourceMap = new Interval[TemplateCompiler.InitialCodeSize];
            _template = string.Empty;
        }

        public string Name
        {
            get
            {
                return _name;
            }

            set
            {
                _name = value;
            }
        }

        public string Template
        {
            get
            {
                return _template;
            }

            set
            {
                _template = value;
            }
        }

        public IToken TemplateDefStartToken
        {
            get
            {
                return _templateDefStartToken;
            }

            set
            {
                _templateDefStartToken = value;
            }
        }

        public ITokenStream Tokens
        {
            get
            {
                return _tokens;
            }

            set
            {
                _tokens = value;
            }
        }

        public CommonTree Ast
        {
            get
            {
                return _ast;
            }

            set
            {
                _ast = value;
            }
        }

        public List<FormalArgument> FormalArguments
        {
            get
            {
                return _formalArguments;
            }

            set
            {
                _formalArguments = value;
                _numberOfArgsWithDefaultValues = (_formalArguments != null) ? _formalArguments.Count(i => i.DefaultValueToken != null) : 0;
            }
        }

        public bool HasFormalArgs
        {
            get
            {
                return _hasFormalArgs;
            }

            set
            {
                _hasFormalArgs = value;
            }
        }

        public List<CompiledTemplate> ImplicitlyDefinedTemplates
        {
            get
            {
                return implicitlyDefinedTemplates;
            }

            set
            {
                implicitlyDefinedTemplates = value;
            }
        }

        public virtual TemplateGroup NativeGroup
        {
            get
            {
                return _nativeGroup;
            }

            set
            {
                _nativeGroup = value;
            }
        }

        public bool IsRegion
        {
            get
            {
                return isRegion;
            }

            set
            {
                isRegion = value;
            }
        }

        public Template.RegionType RegionDefType
        {
            get
            {
                return regionDefType;
            }

            set
            {
                regionDefType = value;
            }
        }

        public bool IsAnonSubtemplate
        {
            get
            {
                return isAnonSubtemplate;
            }

            set
            {
                isAnonSubtemplate = value;
            }
        }

        public virtual string TemplateSource
        {
            get
            {
                Interval r = TemplateRange;
                return Template.Substring(r.Start, r.End - r.Start);
            }
        }

        public virtual Interval TemplateRange
        {
            get
            {
                if (IsAnonSubtemplate)
                {
                    Interval start = sourceMap[0];
                    Interval stop = null;
                    for (int i = sourceMap.Length - 1; i >= 0; i--)
                    {
                        Interval interval = sourceMap[i];
                        if (interval != null)
                        {
                            stop = interval;
                            break;
                        }
                    }

                    return Interval.FromBounds(start.Start, stop.End);
                }

                return new Interval(0, Template.Length);
            }
        }

        public virtual int NumberOfArgsWithDefaultValues
        {
            get
            {
                return _numberOfArgsWithDefaultValues;
            }
        }

        public virtual FormalArgument TryGetFormalArgument(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (FormalArguments == null)
                return null;

            return FormalArguments.FirstOrDefault(i => i.Name == name);
        }

        public virtual void AddImplicitlyDefinedTemplate(CompiledTemplate sub)
        {
            if (ImplicitlyDefinedTemplates == null)
                ImplicitlyDefinedTemplates = new List<CompiledTemplate>();

            ImplicitlyDefinedTemplates.Add(sub);
        }

        public virtual void DefineArgumentDefaultValueTemplates(TemplateGroup group)
        {
            if (FormalArguments == null)
                return;

            foreach (FormalArgument fa in FormalArguments)
            {
                if (fa.DefaultValueToken != null)
                {
                    if (fa.DefaultValueToken.Type == GroupParser.ANONYMOUS_TEMPLATE)
                    {
                        string argSTname = fa.Name + "_default_value";
                        TemplateCompiler c2 = new TemplateCompiler(group);
                        string defArgTemplate = Utility.Strip(fa.DefaultValueToken.Text, 1);
                        fa.CompiledDefaultValue = c2.Compile(group.FileName, argSTname, null, defArgTemplate, fa.DefaultValueToken);
                        fa.CompiledDefaultValue.Name = argSTname;
                        fa.CompiledDefaultValue.DefineImplicitlyDefinedTemplates(group);
                    }
                    else if (fa.DefaultValueToken.Type == GroupParser.STRING)
                    {
                        fa.DefaultValue = Utility.Strip(fa.DefaultValueToken.Text, 1);
                    }
                    else
                    {
                        // true or false
                        fa.DefaultValue = fa.DefaultValueToken.Type == GroupParser.TRUE;
                    }
                }
            }
        }

        public virtual void DefineFormalArguments(IEnumerable<FormalArgument> args)
        {
            HasFormalArgs = true; // even if no args; it's formally defined
            if (args == null)
            {
                FormalArguments = null;
            }
            else
            {
                foreach (FormalArgument a in args)
                    AddArgument(a);
            }
        }

        /** Used by Template.Add() to Add args one by one w/o turning on full formal args definition signal */
        public virtual void AddArgument(FormalArgument a)
        {
            if (FormalArguments == null)
                FormalArguments = new List<FormalArgument>();

            a.Index = FormalArguments.Count;
            FormalArguments.Add(a);
            if (a.DefaultValueToken != null)
                _numberOfArgsWithDefaultValues++;
        }

        public virtual void DefineImplicitlyDefinedTemplates(TemplateGroup group)
        {
            if (ImplicitlyDefinedTemplates != null)
            {
                foreach (CompiledTemplate sub in ImplicitlyDefinedTemplates)
                {
                    group.RawDefineTemplate(sub.Name, sub, sub.TemplateDefStartToken);
                    sub.DefineImplicitlyDefinedTemplates(group);
                }
            }
        }

        public virtual string GetInstructions()
        {
            BytecodeDisassembler dis = new BytecodeDisassembler(this);
            return dis.GetInstructions();
        }

        public virtual void Dump()
        {
            Console.Write(Disassemble());
        }

        public virtual string Disassemble()
        {
            BytecodeDisassembler dis = new BytecodeDisassembler(this);
            using (StringWriter sw = new StringWriter())
            {
                sw.WriteLine(dis.Disassemble());
                sw.WriteLine("Strings:");
                sw.WriteLine(dis.GetStrings());
                sw.WriteLine("Bytecode to template map:");
                sw.WriteLine(dis.GetSourceMap());
                return sw.ToString();
            }
        }
    }
}
