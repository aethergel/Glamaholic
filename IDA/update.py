import idaapi
import ida_bytes
import ida_funcs
import ida_range
import idc

if idaapi.IDA_SDK_VERSION < 900:
    print("This script requires IDA 9.0 or higher.")
    exit(1)

def LocateFunc(name: str | None, signature: str) -> idaapi.ea_t:
    if name is not None:
        ea = idc.get_name_ea(idaapi.get_imagebase(), name)
        if ea != idaapi.BADADDR:
            return ea
    
    return ida_bytes.find_bytes(
        signature,
        idaapi.get_imagebase()
    )

def FuncToInstrArray(ea: idaapi.ea_t) -> list[idaapi.insn_t]:
    ranges = ida_range.rangeset_t()
    fn = idaapi.get_func(ea)
    if not fn:
        raise ValueError(f"Invalid function address {ea}")
    
    if not ida_funcs.get_func_ranges(ranges, fn):
        raise RuntimeError(f"Could not get function ranges for {ea}")
    
    instructions = []
    for i in range(ranges.nranges()):
        rg = ranges.getrange(i)
        
        insn_ea = rg.start_ea
        while insn_ea < rg.end_ea:
            insn = idaapi.insn_t()
            insn_ea += idaapi.decode_insn(insn, insn_ea)
            instructions.append(insn)
    
    return instructions

def FindInsnSeq(instructions: list[idaapi.insn_t], pattern: list[int]) -> list[int]:
    matches = []
    plen = len(pattern)

    for i in range(len(instructions) - plen + 1):
        window = instructions[i:i+plen]
        if all(insn.itype == pattern[j] for j, insn in enumerate(window)):
            matches.append(i)

    return matches

def UpdateTryOnToggle():
    func = LocateFunc(
        "Client::UI::Agent::AgentTryon_ReceiveEvent",
        "48 89 5C 24 ?? 56 57 41 54 41 55 41 57 48 81 EC B0 00 00 00 48 8B D9" # last updated: 7.3.1
    )

    if func == idaapi.BADADDR:
        print("[TryOn Toggle] Error: could not find AgentTryon_ReceiveEvent")
        return

    instructions = FuncToInstrArray(func)
    if len(instructions) == 0:
        print("[TryOn Toggle] Error: no instructions found in AgentTryon_ReceiveEvent")
        return
    
    matches = FindInsnSeq(instructions, [
        idaapi.NN_cmp,
        idaapi.NN_jnz,
        idaapi.NN_mov, 
        idaapi.NN_jmp
    ])
    
    if len(matches) == 0:
        print("[TryOn Toggle] Error: no matching instruction sequences found for TryOn toggle offset")
        return
    
    for match in matches:
        insn = instructions[match]
        print(f"[TryOn Toggle] TryOn toggle offset {insn.Op1.addr} @ {insn.ea:X}")

def UpdateTryOnItems():
    func = LocateFunc(
        "Client::UI::Agent::AgentTryon.TryOn",
        "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 30 8B F9" # last updated: 7.3.1
    )

    if func == idaapi.BADADDR:
        print("[TryOn Items] Error: could not find AgentTryon.TryOn")

    instructions = FuncToInstrArray(func)
    if len(instructions) == 0:
        print("[TryOn Items] Error: no instructions found in AgentTryon.TryOn")
        return
    
    matches = FindInsnSeq(instructions, [
        idaapi.NN_movzx,
        idaapi.NN_movzx,
        idaapi.NN_mov,
        idaapi.NN_movzx,
        idaapi.NN_mov,
        idaapi.NN_mov,
        idaapi.NN_mov,
        idaapi.NN_call
    ])

    if len(matches) != 1:
        print("[TryOn Items] Error: could not find CALL in AgentTryon.TryOn")
        return

    func = instructions[matches[0] + 7].Op1.addr
    
    instructions = FuncToInstrArray(func)
    if len(instructions) == 0:
        print("[TryOn Items] Error: could not fetch instructions in called function")
        return
    
    pattern = [
        idaapi.NN_lea, # array load

        idaapi.NN_cmp,
        idaapi.NN_jz,
        idaapi.NN_cmp,
        idaapi.NN_jb,

        idaapi.NN_inc, # inc idx
        idaapi.NN_add, # add element size
        idaapi.NN_cmp  # checks if end of bounds
    ]

    matches = FindInsnSeq(instructions, pattern)

    if len(matches) != 1:
        print("[TryOn Items] Error: could not find array iteration sequence")
        return

    idx = matches[0]
    array_offset = instructions[idx].Op2.addr
    element_size = instructions[idx + 6].Op2.value
    array_length = instructions[idx + 7].Op2.value
    
    print(f"[TryOn Items] Offset: {array_offset:X}, Element Size: {element_size}, Array Length: {array_length}")

UpdateTryOnToggle()
UpdateTryOnItems()
    