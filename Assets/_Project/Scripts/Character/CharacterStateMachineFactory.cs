public static class CharacterStateMachineFactory
{
    // Role: characterId에 대응되는 캐릭터 상태 머신 인스턴스를 생성한다.
    // Parameters:
    // - characterId: 생성할 캐릭터 ID
    public static ICharacterStateMachine Create(byte characterId)
    {
        return new Default_CharacterStateMachine(characterId);
    }
}
