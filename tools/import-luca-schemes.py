#!/usr/bin/env python3
"""Convert pinned LuckSystem layouts into SchemeTool data; never execute plugins."""
import argparse
import ast
import json
from pathlib import Path

ENCODINGS={'Charset_UTF8':'utf-8','Charset_Unicode':'utf-16','Charset_SJIS':'shift_jis','expr':'expression','text':'text'}

def layout(statements):
    result=[]
    for node in statements:
        target=None
        if isinstance(node,ast.Assign):
            target=node.targets[0].id
            node=ast.Expr(value=node.value)
        if isinstance(node,ast.Expr):
            call=node.value
            if isinstance(call,ast.Constant) and isinstance(call.value,str):continue
            if not isinstance(call,ast.Call) or not isinstance(call.func,ast.Attribute) or not isinstance(call.func.value,ast.Name) or call.func.value.id!='core':
                raise ValueError(ast.dump(node))
            name=call.func.attr
            if name=='end':continue
            if name in ('read_uint8','read_uint16','read_uint32'):
                token={'read_uint8':'u8','read_uint16':'u16','read_uint32':'u32'}[name]
            elif name in ('read_str','read_len_str'):
                encoding=ENCODINGS[call.args[0].attr] if call.args and isinstance(call.args[0],ast.Attribute) else 'text'
                token=('s:' if name=='read_str' else 'l:')+encoding
            elif name=='read_jump':token='j:'+ (call.args[0].id if call.args else '')
            elif name=='read':token='raw'
            else:raise ValueError(name)
            result.append(token+('@'+target if target else ''))
        elif isinstance(node,ast.If):
            if node.orelse:raise ValueError('Unexpected else')
            test=node.test
            if isinstance(test,ast.Call) and test.func.attr=='can_read':token='remaining:0'
            elif isinstance(test,ast.Compare) and len(test.ops)==1 and isinstance(test.ops[0],ast.Gt):
                call=test.left
                if isinstance(call.func,ast.Name) and call.func.id=='len':token='nonempty:'+call.args[0].id
                elif isinstance(call.func,ast.Attribute) and call.func.attr=='remaining_length':token='remaining:'+str(ast.literal_eval(test.comparators[0]))
                else:raise ValueError(ast.dump(test))
            else:raise ValueError(ast.dump(test))
            result.extend([token,*layout(node.body),'end'])
        elif isinstance(node,ast.While) and isinstance(node.test,ast.Call) and node.test.func.attr=='can_read':
            result.extend(['while',*layout(node.body),'end'])
        elif isinstance(node,ast.For) and isinstance(node.iter,ast.Call) and node.iter.func.id=='range' and len(node.iter.args)==1:
            result.extend(['repeat:'+node.iter.args[0].id,*layout(node.body),'end'])
        else:raise ValueError(ast.dump(node))
    return result

def profile(path):
    tree=ast.parse(path.read_text(encoding='utf-8-sig'))
    result={'ExpressionEncoding':'utf-8','TextEncoding':'utf-16','Parameters':{}}
    for node in tree.body:
        if isinstance(node,ast.ImportFrom) and node.module.startswith('base.'):
            inherited=profile(path.parent/(node.module.replace('.','/')+'.py'))
            result['Parameters'].update(inherited['Parameters'])
        if isinstance(node,ast.FunctionDef):
            if node.name=='Init':
                for call in ast.walk(node):
                    if isinstance(call,ast.Call) and isinstance(call.func,ast.Attribute) and call.func.attr=='set_config':
                        for keyword in call.keywords:
                            if keyword.arg in ('expr_charset','text_charset'):
                                result['ExpressionEncoding' if keyword.arg=='expr_charset' else 'TextEncoding']=ENCODINGS[keyword.value.attr]
            else:result['Parameters'][node.name]=layout(node.body)
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path)
    parser.add_argument('output',type=Path)
    args=parser.parse_args()
    schemes={}
    for path in sorted(args.source.glob('*.py')):
        item=profile(path)
        opcode_file=path.with_suffix('.txt')
        if not opcode_file.exists():opcode_file=path.parent/path.stem/'OPCODE.txt'
        item['Opcodes']=opcode_file.read_text(encoding='utf-8-sig').splitlines()
        schemes[path.stem]=item
    # LB_EN uses native Go handlers rather than the Python plugin interface.
    base={'IFN':['s:expression','j:'],'IFY':['s:expression','j:'],'GOTO':['j:'],
          'GOSUB':['u16','j:'],'FARCALL':['u16','s:expression@file','j:file'],
          'JUMP':['s:expression@file','remaining:0','j:file','end']}
    lb={'ExpressionEncoding':'shift_jis','TextEncoding':'utf-16',
        'Opcodes':(args.source/'LB_EN/OPCODE.txt').read_text(encoding='utf-8-sig').splitlines(),
        'Parameters':dict(base,MESSAGE=['u16','s:text','s:text','u8'],SELECT=['u16','u16','u16','u16','s:text','s:text','raw'],
          SAYAVOICETEXT=['u16','s:text','s:text'],VARSTR_SET=['u16','s:text'],EQU=['u16','s:expression'],
          ADD=['u16','s:expression'],RANDOM=['u16','s:expression','s:expression'])}
    def when(condition,*body):return [condition,*body,'end']
    lb['Parameters']['BATTLE']=['u16@battle',*when('remaining:0',
        *when('equal:battle:301,302','u16'),*when('equal:battle:300','s:expression'),
        *when('equal:battle:101','u16','peek16@mode',
            *when('equal:mode:0','u16','u16','s:expression','s:text','s:text'),
            *when('notequal:mode:0','s:text','s:text')),
        *when('equal:battle:102','peek16@mode',
            *when('equal:mode:0','u16','u16','s:expression','s:text','s:text'),
            *when('notequal:mode:0','s:text','s:text')))]
    lb['Parameters']['TASK']=['u16@task',*when('remaining:0',
        *when('equal:task:4','u16@mode',*when('remaining:0',
            *when('equal:mode:0,4,5','u16','s:text','s:text'),
            *when('equal:mode:1','u16','u16','u16','s:text','s:text','s:text','s:text'),
            *when('equal:mode:6','u16','u16','s:text','s:text'))),
        *when('equal:task:54','s:text'),
        *when('equal:task:23','u16@mode',*when('remaining:0',
            *when('equal:mode:12835,13859','s:expression','s:expression','s:expression'),
            *when('equal:mode:12589','u16','s:expression','s:expression'))),
        *when('equal:task:69','u16','s:text','s:text','s:text','s:text'),
        *when('equal:task:28','u16@mode',*when('remaining:0',
            *when('equal:mode:200,201,202,203,204,210,400','s:expression','s:expression','s:expression'))))]
    schemes['LB_EN']=lb
    for item in schemes.values():
        item['Parameters'].setdefault('ONGOTO',['s:expression','while','u16','j:','end'])
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(schemes,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Prepared',len(schemes),'Luca schemes')

if __name__=='__main__':main()
